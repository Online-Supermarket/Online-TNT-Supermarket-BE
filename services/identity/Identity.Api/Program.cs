using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Identity") ?? "Host=localhost;Port=5432;Database=marketflow;Username=marketflow;Password=marketflow;Search Path=identity"));
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<RequestMetrics>();
var app = builder.Build();
app.Use(async (ctx, next) => { var correlation = ctx.Request.Headers["X-Correlation-Id"].FirstOrDefault() ?? Guid.NewGuid().ToString(); ctx.Response.Headers["X-Correlation-Id"] = correlation; var stopwatch = System.Diagnostics.Stopwatch.StartNew(); using (app.Logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = correlation })) { try { await next(); } finally { ctx.RequestServices.GetRequiredService<RequestMetrics>().Record(ctx.Response.StatusCode, stopwatch.Elapsed); } } });
var applyMigrations = app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("Migrations:ApplyOnStartup");
if (applyMigrations)
    await Database.InitializeAsync(app.Services.GetRequiredService<NpgsqlDataSource>(), app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("Seed:DemoData"));
if (applyMigrations && builder.Configuration.GetValue<bool>("Migrations:ExitAfterApply")) return;
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "identity" }));
app.MapGet("/health/ready", async (NpgsqlDataSource db) =>
{
    try { await using var cmd = db.CreateCommand("SELECT 1"); await cmd.ExecuteScalarAsync(); return Results.Ok(new { status = "ready", service = "identity", dependencies = new { postgres = "ready" } }); }
    catch { return Results.Problem("PostgreSQL is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable); }
});
app.MapGet("/metrics", (RequestMetrics m) => Results.Text(m.AsPrometheus("identity"), "text/plain"));
app.MapGet("/openapi/v1.json", () => Results.Text(OpenApi.Document("MarketFlow Identity API", new("get", "/health", "Liveness check"), new("get", "/health/ready", "Dependency readiness check"), new("post", "/auth/login", "Sign in"), new("post", "/auth/register", "Register a customer"), new("post", "/auth/logout", "Revoke the current session"), new("get", "/auth/introspect", "Inspect the current token"), new("get", "/users/me", "Read the current account"), new("get", "/admin/users", "List accounts"), new("post", "/admin/users", "Create a staff account")), "application/json"));
app.MapGet("/swagger", () => Results.Content(OpenApi.Ui, "text/html"));

app.MapGet("/riders/available", async (string? zone, HttpRequest request, NpgsqlDataSource db, TokenService tokens) =>
{
    var principal = await IdentityAuth.Principal(request, db, tokens);
    if (principal is null || (!principal.Roles.Contains("Staff") && !principal.Roles.Contains("Admin"))) return Results.StatusCode(403);
    await using var cmd = db.CreateCommand(@"SELECT u.id,u.full_name,u.display_name,u.contact_number,u.district,u.availability_status,p.vehicle_type,p.vehicle_model,p.vehicle_number,p.license_number
        FROM users u LEFT JOIN rider_profiles p ON p.user_id=u.id WHERE 'Rider'=ANY(u.roles) AND u.active=true AND COALESCE(u.availability_status,'Available')='Available'
        AND ($1 IS NULL OR lower(COALESCE(u.district,''))=lower($1)) ORDER BY COALESCE(u.full_name,u.display_name)");
    // PostgreSQL cannot infer the type of a null value used in "$1 IS NULL".
    // Always bind this optional filter as text so an unfiltered rider query works.
    cmd.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Text, (object?)zone?.Trim() ?? DBNull.Value);
    await using var reader = await cmd.ExecuteReaderAsync(); var rows = new List<object>();
    while (await reader.ReadAsync()) rows.Add(new { riderId = reader.GetGuid(0), fullName = reader.IsDBNull(1) ? reader.GetString(2) : reader.GetString(1), phoneNumber = reader.IsDBNull(3) ? null : reader.GetString(3), district = reader.IsDBNull(4) ? null : reader.GetString(4), availabilityStatus = reader.IsDBNull(5) ? "Available" : reader.GetString(5), vehicleType = reader.IsDBNull(6) ? null : reader.GetString(6), vehicleModel = reader.IsDBNull(7) ? null : reader.GetString(7), vehicleNumber = reader.IsDBNull(8) ? null : reader.GetString(8), licenseNumber = reader.IsDBNull(9) ? null : reader.GetString(9), active = true });
    return Results.Ok(rows);
});

app.MapPost("/auth/login", async (LoginRequest request, NpgsqlDataSource db, TokenService tokens) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["credentials"] = ["Email and password are required."] });
    await using var cmd = db.CreateCommand("SELECT id,email,display_name,password_hash,roles,active FROM users WHERE lower(email)=lower($1)"); cmd.Parameters.AddWithValue(request.Email.Trim());
    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync() || !reader.GetBoolean(5) || !Password.Verify(request.Password, reader.GetString(3))) return Results.Unauthorized();
    var user = new User(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), IdentityRoles.NormalizeAll(reader.GetFieldValue<string[]>(4))); var token = tokens.Create(user); await reader.CloseAsync();
    await using var session = db.CreateCommand("INSERT INTO sessions(token_hash,user_id,expires_at) VALUES ($1,$2,$3)"); session.Parameters.AddWithValue(TokenService.Hash(token)); session.Parameters.AddWithValue(user.Id); session.Parameters.AddWithValue(DateTimeOffset.UtcNow.AddHours(8)); await session.ExecuteNonQueryAsync();
    return Results.Ok(new { accessToken = token, tokenType = "Bearer", expiresInSeconds = 28800, user = new { user.Id, user.Email, user.DisplayName, roles = user.Roles } });
});
app.MapPost("/auth/register", async (RegisterRequest request, NpgsqlDataSource db, TokenService tokens) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.DisplayName) || string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8) return Results.ValidationProblem(new Dictionary<string, string[]> { ["registration"] = ["Name, email and a password of at least 8 characters are required."] });
    var profileErrors = IdentityValidation.Profile(request.IdNumber, request.ContactNumber);
    if (profileErrors.Count > 0) return Results.ValidationProblem(profileErrors);
    var user = new User(Guid.NewGuid(), request.Email.Trim(), request.DisplayName.Trim(), ["Customer"]);
    try { await using var cmd = db.CreateCommand("INSERT INTO users(id,email,display_name,password_hash,roles,full_name,id_number,contact_number,district,address) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10)"); cmd.Parameters.AddWithValue(user.Id); cmd.Parameters.AddWithValue(user.Email); cmd.Parameters.AddWithValue(user.DisplayName); cmd.Parameters.AddWithValue(Password.Hash(request.Password)); cmd.Parameters.AddWithValue(user.Roles); cmd.Parameters.AddWithValue((object?)request.FullName?.Trim() ?? DBNull.Value); cmd.Parameters.AddWithValue((object?)request.IdNumber?.Trim() ?? DBNull.Value); cmd.Parameters.AddWithValue((object?)request.ContactNumber?.Trim() ?? DBNull.Value); cmd.Parameters.AddWithValue((object?)request.District?.Trim() ?? DBNull.Value); cmd.Parameters.AddWithValue((object?)request.Address?.Trim() ?? DBNull.Value); await cmd.ExecuteNonQueryAsync(); var token = tokens.Create(user); await using var session = db.CreateCommand("INSERT INTO sessions(token_hash,user_id,expires_at) VALUES($1,$2,$3)"); session.Parameters.AddWithValue(TokenService.Hash(token)); session.Parameters.AddWithValue(user.Id); session.Parameters.AddWithValue(DateTimeOffset.UtcNow.AddHours(8)); await session.ExecuteNonQueryAsync(); return Results.Created("/users/me", new { accessToken = token, tokenType = "Bearer", expiresInSeconds = 28800, user = new { user.Id, user.Email, user.DisplayName, roles = user.Roles } }); }
    catch (PostgresException e) when (e.SqlState == "23505") { return Results.Conflict(new { message = "An account with that email already exists." }); }
});
app.MapPost("/auth/logout", async (HttpRequest request, NpgsqlDataSource db) => { var token = TokenService.GetBearer(request); if (token is null) return Results.Unauthorized(); await using var cmd = db.CreateCommand("UPDATE sessions SET revoked_at=now() WHERE token_hash=$1 AND revoked_at IS NULL"); cmd.Parameters.AddWithValue(TokenService.Hash(token)); await cmd.ExecuteNonQueryAsync(); return Results.NoContent(); });
app.MapGet("/auth/introspect", async (HttpRequest request, NpgsqlDataSource db, TokenService tokens) =>
{
    var token = TokenService.GetBearer(request); if (token is null || !tokens.TryRead(token, out var payload)) return Results.Ok(new { active = false });
    await using var cmd = db.CreateCommand("SELECT u.email,u.display_name,u.roles FROM sessions s JOIN users u ON u.id=s.user_id WHERE s.token_hash=$1 AND s.revoked_at IS NULL AND s.expires_at>now() AND u.active=true"); cmd.Parameters.AddWithValue(TokenService.Hash(token)); await using var reader = await cmd.ExecuteReaderAsync();
    return await reader.ReadAsync() ? Results.Ok(new { active = true, subject = payload.Subject, email = reader.GetString(0), displayName = reader.GetString(1), roles = IdentityRoles.NormalizeAll(reader.GetFieldValue<string[]>(2)), expiresAt = payload.ExpiresAt }) : Results.Ok(new { active = false });
});
app.MapGet("/users/me", async (HttpRequest request, NpgsqlDataSource db, TokenService tokens) => { var token = TokenService.GetBearer(request); if (token is null || !tokens.TryRead(token, out var payload)) return Results.StatusCode(401); await using var cmd = db.CreateCommand("SELECT u.email,u.display_name,u.roles,u.full_name,u.contact_number,u.district,u.address FROM sessions s JOIN users u ON u.id=s.user_id WHERE s.token_hash=$1 AND s.revoked_at IS NULL AND s.expires_at>now() AND u.active=true"); cmd.Parameters.AddWithValue(TokenService.Hash(token)); await using var r = await cmd.ExecuteReaderAsync(); return await r.ReadAsync() ? Results.Ok(new { id = payload.Subject, email = r.GetString(0), displayName = r.GetString(1), roles = IdentityRoles.NormalizeAll(r.GetFieldValue<string[]>(2)), fullName = r.IsDBNull(3) ? null : r.GetString(3), contactNumber = r.IsDBNull(4) ? null : r.GetString(4), district = r.IsDBNull(5) ? null : r.GetString(5), address = r.IsDBNull(6) ? null : r.GetString(6) }) : Results.StatusCode(401); });
// Operational staff may look up the limited customer/rider contact data needed to fulfil an order.
// Administration remains restricted to /admin/users; credentials and administrative fields are never exposed here.
app.MapGet("/users", async (HttpRequest request, NpgsqlDataSource db, TokenService tokens) =>
{
    var actor = await IdentityAuth.Principal(request, db, tokens);
    if (actor is null || !actor.Roles.Any(IdentityRoles.IsStaffOrAdmin)) return Results.StatusCode(403);
    await using var cmd = db.CreateCommand(@"SELECT id,email,display_name,roles,full_name,contact_number,district
        FROM identity.users WHERE active=true AND ('Customer'=ANY(roles) OR 'Rider'=ANY(roles)) ORDER BY display_name");
    await using var reader = await cmd.ExecuteReaderAsync(); var users = new List<object>();
    while (await reader.ReadAsync()) users.Add(new { id = reader.GetGuid(0), email = reader.GetString(1), displayName = reader.GetString(2), roles = IdentityRoles.NormalizeAll(reader.GetFieldValue<string[]>(3)), fullName = reader.IsDBNull(4) ? null : reader.GetString(4), contactNumber = reader.IsDBNull(5) ? null : reader.GetString(5), district = reader.IsDBNull(6) ? null : reader.GetString(6) });
    return Results.Ok(users);
});
app.MapGet("/admin/users", async (string? role, HttpRequest request, NpgsqlDataSource db, TokenService tokens) =>
{
    if (!await IdentityAuth.IsAdmin(request, db, tokens)) return Results.StatusCode(403);
    var sql = @"SELECT u.id,u.email,u.display_name,u.roles,u.active,u.created_at,u.full_name,u.contact_number,
                       u.assigned_store,u.availability_status,u.updated_at,u.district,u.address,
                       rp.vehicle_type,rp.vehicle_model,rp.vehicle_number,rp.license_number
                FROM users u
                LEFT JOIN identity.rider_profiles rp ON rp.user_id = u.id";
    if (!string.IsNullOrWhiteSpace(role) && !IdentityRoles.All.Contains(role))
        return Results.BadRequest(new { message = "Unsupported role." });
    if (!string.IsNullOrWhiteSpace(role)) sql += " WHERE $1 = ANY(u.roles)";
    sql += " ORDER BY u.created_at DESC";
    await using var cmd = db.CreateCommand(sql);
    if (!string.IsNullOrWhiteSpace(role)) cmd.Parameters.AddWithValue(role);
    await using var reader = await cmd.ExecuteReaderAsync(); var rows = new List<object>();
    while (await reader.ReadAsync())
    {
        rows.Add(new {
            id = reader.GetGuid(0),
            riderId = reader.GetGuid(0),
            email = reader.GetString(1),
            displayName = reader.GetString(2),
            roles = IdentityRoles.NormalizeAll(reader.GetFieldValue<string[]>(3)),
            active = reader.GetBoolean(4),
            createdAt = reader.GetFieldValue<DateTimeOffset>(5),
            fullName = reader.IsDBNull(6) ? null : reader.GetString(6),
            contactNumber = reader.IsDBNull(7) ? null : reader.GetString(7),
            phoneNumber = reader.IsDBNull(7) ? null : reader.GetString(7),
            assignedStore = reader.IsDBNull(8) ? null : reader.GetString(8),
            availabilityStatus = reader.IsDBNull(9) ? "Available" : reader.GetString(9),
            updatedAt = reader.IsDBNull(10) ? reader.GetFieldValue<DateTimeOffset>(5) : reader.GetFieldValue<DateTimeOffset>(10),
            district = reader.IsDBNull(11) ? null : reader.GetString(11),
            address = reader.IsDBNull(12) ? null : reader.GetString(12),
            vehicleType = reader.IsDBNull(13) ? null : reader.GetString(13),
            vehicleModel = reader.IsDBNull(14) ? null : reader.GetString(14),
            vehicleNumber = reader.IsDBNull(15) ? null : reader.GetString(15),
            licenseNumber = reader.IsDBNull(16) ? null : reader.GetString(16)
        });
    }
    return Results.Ok(rows);
});
app.MapGet("/admin/users/{id:guid}", async (Guid id, HttpRequest request, NpgsqlDataSource db, TokenService tokens) =>
{
    if (!await IdentityAuth.IsAdmin(request, db, tokens)) return Results.StatusCode(403);
    await using var cmd = db.CreateCommand(@"SELECT u.id,u.email,u.display_name,u.roles,u.active,u.created_at,u.full_name,u.contact_number,
                       u.assigned_store,u.availability_status,u.updated_at,u.district,u.address,
                       rp.vehicle_type,rp.vehicle_model,rp.vehicle_number,rp.license_number
                FROM users u
                LEFT JOIN identity.rider_profiles rp ON rp.user_id = u.id
                WHERE u.id=$1");
    cmd.Parameters.AddWithValue(id);
    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return Results.NotFound();
    return Results.Ok(new {
        id = reader.GetGuid(0),
        riderId = reader.GetGuid(0),
        email = reader.GetString(1),
        displayName = reader.GetString(2),
        roles = IdentityRoles.NormalizeAll(reader.GetFieldValue<string[]>(3)),
        active = reader.GetBoolean(4),
        createdAt = reader.GetFieldValue<DateTimeOffset>(5),
        fullName = reader.IsDBNull(6) ? null : reader.GetString(6),
        contactNumber = reader.IsDBNull(7) ? null : reader.GetString(7),
        phoneNumber = reader.IsDBNull(7) ? null : reader.GetString(7),
        assignedStore = reader.IsDBNull(8) ? null : reader.GetString(8),
        availabilityStatus = reader.IsDBNull(9) ? "Available" : reader.GetString(9),
        updatedAt = reader.IsDBNull(10) ? reader.GetFieldValue<DateTimeOffset>(5) : reader.GetFieldValue<DateTimeOffset>(10),
        district = reader.IsDBNull(11) ? null : reader.GetString(11),
        address = reader.IsDBNull(12) ? null : reader.GetString(12),
        vehicleType = reader.IsDBNull(13) ? null : reader.GetString(13),
        vehicleModel = reader.IsDBNull(14) ? null : reader.GetString(14),
        vehicleNumber = reader.IsDBNull(15) ? null : reader.GetString(15),
        licenseNumber = reader.IsDBNull(16) ? null : reader.GetString(16)
    });
});
app.MapPost("/admin/users", async (AdminUserRequest request, HttpRequest http, NpgsqlDataSource db, TokenService tokens) =>
{
    if (!await IdentityAuth.IsAdmin(http, db, tokens)) return Results.StatusCode(403);
    var errors = AdminUserRequest.Validate(request); if (errors.Count > 0) return Results.ValidationProblem(errors);
    var user = new User(Guid.NewGuid(), request.Email.Trim(), string.IsNullOrWhiteSpace(request.FullName) ? request.DisplayName.Trim() : request.FullName.Trim(), IdentityRoles.NormalizeAll(request.Roles));
    await using var conn = await db.OpenConnectionAsync();
    await using var tx = await conn.BeginTransactionAsync();
    try
    {
        await using (var cmd = new NpgsqlCommand("INSERT INTO identity.users(id,email,display_name,password_hash,roles,full_name,contact_number,district,address,assigned_store,availability_status,active,updated_at) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,now())", conn, tx))
        {
            cmd.Parameters.AddWithValue(user.Id); cmd.Parameters.AddWithValue(user.Email); cmd.Parameters.AddWithValue(user.DisplayName); cmd.Parameters.AddWithValue(Password.Hash(request.Password)); cmd.Parameters.AddWithValue(user.Roles);
            cmd.Parameters.AddWithValue((object?)request.FullName?.Trim() ?? DBNull.Value);
            cmd.Parameters.AddWithValue((object?)request.ContactNumber?.Trim() ?? DBNull.Value);
            cmd.Parameters.AddWithValue((object?)request.District?.Trim() ?? DBNull.Value);
            cmd.Parameters.AddWithValue((object?)request.Address?.Trim() ?? DBNull.Value);
            cmd.Parameters.AddWithValue((object?)request.AssignedStore?.Trim() ?? DBNull.Value);
            cmd.Parameters.AddWithValue(string.IsNullOrWhiteSpace(request.AvailabilityStatus) ? "Available" : request.AvailabilityStatus.Trim());
            cmd.Parameters.AddWithValue(request.Active);
            await cmd.ExecuteNonQueryAsync();
        }
        // A Rider always receives a matching vehicle profile in the same transaction.
        var vt = request.VehicleType?.Trim() ?? "";
        var vm = request.VehicleModel?.Trim() ?? "";
        var vn = RiderVehicle.NormalizeIdentifier(request.VehicleNumber);
        var ln = RiderVehicle.NormalizeIdentifier(request.LicenseNumber);
        if (RiderVehicle.IsRider(user.Roles))
        {
            await using var profCmd = new NpgsqlCommand(@"INSERT INTO identity.rider_profiles(user_id,vehicle_type,vehicle_model,vehicle_number,license_number)
                VALUES($1,$2,$3,$4,$5)
                ON CONFLICT(user_id) DO UPDATE SET vehicle_type=$2, vehicle_model=$3, vehicle_number=$4, license_number=$5, updated_at=now()", conn, tx);
            profCmd.Parameters.AddWithValue(user.Id);
            profCmd.Parameters.AddWithValue(vt);
            profCmd.Parameters.AddWithValue(vm);
            profCmd.Parameters.AddWithValue(vn);
            profCmd.Parameters.AddWithValue(ln);
            await profCmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
        return Results.Created($"/admin/users/{user.Id}", new { id = user.Id });
    }
    catch (PostgresException e) when (e.SqlState == "23505")
    {
        await tx.RollbackAsync();
        var msg = e.ConstraintName switch
        {
            "uq_rider_profiles_vehicle_number" => "Vehicle number is already assigned to another Rider.",
            "uq_rider_profiles_license_number" => "License number is already assigned to another Rider.",
            _ => "An account with that email already exists."
        };
        return Results.Conflict(new { message = msg });
    }
    catch (Exception ex) { await tx.RollbackAsync(); return Results.Problem(ex.Message); }
});
app.MapPut("/admin/users/{id:guid}", async (Guid id, AdminUserUpdateRequest request, HttpRequest http, NpgsqlDataSource db, TokenService tokens) =>
{
    var admin = await IdentityAuth.Principal(http, db, tokens); if (admin is null || (!admin.Roles.Contains("Admin"))) return Results.StatusCode(403);
    if (request.Roles is not null && !IdentityRoles.IsValidAssignment(request.Roles))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["roles"] = ["Use Admin, Staff, Rider or Customer."] });
    if (!string.IsNullOrWhiteSpace(request.Email) && !request.Email.Contains('@')) return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["Valid email is required."] });

    await using var conn = await db.OpenConnectionAsync();
    await using var tx = await conn.BeginTransactionAsync();
    try
    {
        await using (var checkCmd = new NpgsqlCommand("SELECT roles, active FROM identity.users WHERE id=$1", conn, tx))
        {
            checkCmd.Parameters.AddWithValue(id);
            await using var reader = await checkCmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) { await tx.RollbackAsync(); return Results.NotFound(); }
            var curRoles = reader.GetFieldValue<string[]>(0);
            var curActive = reader.GetBoolean(1);
            await reader.CloseAsync();

            if (admin.Id == id && request.Active == false) { await tx.RollbackAsync(); return Results.Conflict(new { message = "An administrator cannot deactivate their own account." }); }

            var newRoles = request.Roles != null && request.Roles.Length > 0 ? IdentityRoles.NormalizeAll(request.Roles) : curRoles;
            if (admin.Id == id && !newRoles.Contains("Admin")) { await tx.RollbackAsync(); return Results.Conflict(new { message = "An administrator cannot remove their own Admin role." }); }

            var newActive = request.Active ?? curActive;
            var newDisplayName = string.IsNullOrWhiteSpace(request.FullName) ? null : request.FullName.Trim();
            if (request.HasVehicleFields)
            {
                if (!RiderVehicle.IsRider(newRoles)) { await tx.RollbackAsync(); return Results.ValidationProblem(new Dictionary<string, string[]> { ["vehicle"] = ["Vehicle information can only be set for a Rider."] }); }
                var vehicleErrors = RiderVehicle.Validate(request.VehicleType, request.VehicleModel, request.VehicleNumber, request.LicenseNumber);
                if (vehicleErrors.Count > 0) { await tx.RollbackAsync(); return Results.ValidationProblem(vehicleErrors); }
            }

            await using var updateCmd = new NpgsqlCommand(@"
                UPDATE identity.users SET
                    email = COALESCE(NULLIF($2, ''), email),
                    display_name = COALESCE(NULLIF($3, ''), display_name),
                    full_name = COALESCE(NULLIF($3, ''), full_name),
                    contact_number = $4,
                    district = $5,
                    address = $6,
                    assigned_store = $7,
                    active = $8,
                    availability_status = COALESCE(NULLIF($9, ''), availability_status),
                    roles = $10,
                    updated_at = now()
                WHERE id = $1", conn, tx);
            updateCmd.Parameters.AddWithValue(id);
            updateCmd.Parameters.AddWithValue((object?)request.Email?.Trim() ?? DBNull.Value);
            updateCmd.Parameters.AddWithValue((object?)newDisplayName ?? DBNull.Value);
            updateCmd.Parameters.AddWithValue((object?)request.ContactNumber?.Trim() ?? DBNull.Value);
            updateCmd.Parameters.AddWithValue((object?)request.District?.Trim() ?? DBNull.Value);
            updateCmd.Parameters.AddWithValue((object?)request.Address?.Trim() ?? DBNull.Value);
            updateCmd.Parameters.AddWithValue((object?)request.AssignedStore?.Trim() ?? DBNull.Value);
            updateCmd.Parameters.AddWithValue(newActive);
            updateCmd.Parameters.AddWithValue((object?)request.AvailabilityStatus?.Trim() ?? DBNull.Value);
            updateCmd.Parameters.AddWithValue(newRoles);
            var affected = await updateCmd.ExecuteNonQueryAsync();
            if (affected == 0) { await tx.RollbackAsync(); return Results.NotFound(); }
        }

        // Upsert vehicle profile if any vehicle field is supplied
        var vt = request.VehicleType?.Trim();
        var vm = request.VehicleModel?.Trim();
        var vn = RiderVehicle.NormalizeIdentifier(request.VehicleNumber);
        var ln = RiderVehicle.NormalizeIdentifier(request.LicenseNumber);
        if (request.HasVehicleFields)
        {
            await using var profCmd = new NpgsqlCommand(@"INSERT INTO identity.rider_profiles(user_id,vehicle_type,vehicle_model,vehicle_number,license_number)
                VALUES($1,$2,$3,$4,$5)
                ON CONFLICT(user_id) DO UPDATE SET
                    vehicle_type   = $2,
                    vehicle_model  = $3,
                    vehicle_number = $4,
                    license_number = $5,
                    updated_at     = now()", conn, tx);
            profCmd.Parameters.AddWithValue(id);
            profCmd.Parameters.AddWithValue(vt ?? "");
            profCmd.Parameters.AddWithValue(vm ?? "");
            profCmd.Parameters.AddWithValue(vn ?? "");
            profCmd.Parameters.AddWithValue(ln ?? "");
            await profCmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        return Results.NoContent();
    }
    catch (PostgresException e) when (e.SqlState == "23505")
    {
        await tx.RollbackAsync();
        var msg = e.ConstraintName switch
        {
            "uq_rider_profiles_vehicle_number" => "Vehicle number is already assigned to another Rider.",
            "uq_rider_profiles_license_number" => "License number is already assigned to another Rider.",
            _ => "An account with that email already exists."
        };
        return Results.Conflict(new { message = msg });
    }
    catch (Exception ex) { await tx.RollbackAsync(); return Results.Problem(ex.Message); }
});
app.MapPatch("/admin/users/{id:guid}/status", async (Guid id, UserStatusRequest request, HttpRequest http, NpgsqlDataSource db, TokenService tokens) =>
{
    var admin = await IdentityAuth.Principal(http, db, tokens); if (admin is null || (!admin.Roles.Contains("Admin"))) return Results.StatusCode(403);
    if (admin.Id == id && !request.Active) return Results.Conflict(new { message = "An administrator cannot deactivate their own account." });
    await using var cmd = db.CreateCommand("UPDATE users SET active=$2, updated_at=now() WHERE id=$1");
    cmd.Parameters.AddWithValue(id);
    cmd.Parameters.AddWithValue(request.Active);
    return await cmd.ExecuteNonQueryAsync() == 1 ? Results.NoContent() : Results.NotFound();
});
app.MapPatch("/admin/users/{id:guid}/availability", async (Guid id, UserAvailabilityRequest request, HttpRequest http, NpgsqlDataSource db, TokenService tokens) =>
{
    var admin = await IdentityAuth.Principal(http, db, tokens); if (admin is null || (!admin.Roles.Contains("Admin") && !admin.Roles.Contains("Staff") && !(admin.Roles.Contains("Rider") && admin.Id == id))) return Results.StatusCode(403);
    var valid = new[] { "Available", "Busy", "Offline" };
    if (!valid.Contains(request.AvailabilityStatus, StringComparer.OrdinalIgnoreCase))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["availabilityStatus"] = ["AvailabilityStatus must be Available, Busy, or Offline."] });
    await using var cmd = db.CreateCommand("UPDATE users SET availability_status=$2, updated_at=now() WHERE id=$1");
    cmd.Parameters.AddWithValue(id);
    cmd.Parameters.AddWithValue(request.AvailabilityStatus);
    return await cmd.ExecuteNonQueryAsync() == 1 ? Results.NoContent() : Results.NotFound();
});
app.MapPut("/admin/users/{id:guid}/roles", async (Guid id, RoleUpdateRequest request, HttpRequest http, NpgsqlDataSource db, TokenService tokens) =>
{
    var admin = await IdentityAuth.Principal(http, db, tokens); if (admin is null || (!admin.Roles.Contains("Admin"))) return Results.StatusCode(403);
    var roles = IdentityRoles.NormalizeAll(request.Roles ?? []);
    if (!IdentityRoles.IsValidAssignment(request.Roles)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["roles"] = ["Provide one or more supported roles."] });
    if (admin.Id == id && !roles.Contains("Admin")) return Results.Conflict(new { message = "An administrator cannot remove their own Admin role." });
    await using var cmd = db.CreateCommand("UPDATE users SET roles=$2, updated_at=now() WHERE id=$1 AND active=true"); cmd.Parameters.AddWithValue(id); cmd.Parameters.AddWithValue(roles);
    return await cmd.ExecuteNonQueryAsync() == 1 ? Results.NoContent() : Results.NotFound();
});
app.MapDelete("/admin/users/{id:guid}", async (Guid id, HttpRequest http, NpgsqlDataSource db, TokenService tokens) =>
{
    var admin = await IdentityAuth.Principal(http, db, tokens);
    if (admin is null || (!admin.Roles.Contains("Admin"))) return Results.StatusCode(403);
    if (admin.Id == id) return Results.Conflict(new { message = "An administrator cannot delete their own account." });

    await using var conn = await db.OpenConnectionAsync();
    await using var tx = await conn.BeginTransactionAsync();
    try
    {
        await using (var sessCmd = new NpgsqlCommand("DELETE FROM sessions WHERE user_id=$1", conn, tx))
        {
            sessCmd.Parameters.AddWithValue(id);
            await sessCmd.ExecuteNonQueryAsync();
        }

        await using (var userCmd = new NpgsqlCommand("DELETE FROM users WHERE id=$1", conn, tx))
        {
            userCmd.Parameters.AddWithValue(id);
            var affected = await userCmd.ExecuteNonQueryAsync();
            if (affected == 0)
            {
                await tx.RollbackAsync();
                return Results.NotFound();
            }
        }

        await tx.CommitAsync();
        return Results.NoContent();
    }
    catch (Exception ex)
    {
        await tx.RollbackAsync();
        return Results.Problem(ex.Message);
    }
});
app.Run();

record LoginRequest(string Email, string Password);
record RegisterRequest(string Email, string DisplayName, string Password, string? FullName = null, string? IdNumber = null, string? ContactNumber = null, string? District = null, string? Address = null);
record AdminUserRequest(string Email, string DisplayName, string Password, string[] Roles, string? FullName = null, string? ContactNumber = null, string? AssignedStore = null, string? AvailabilityStatus = null, bool Active = true, string? VehicleType = null, string? VehicleNumber = null, string? LicenseNumber = null, string? District = null, string? Address = null, string? VehicleModel = null, string? IdNumber = null)
{
    public static Dictionary<string, string[]> Validate(AdminUserRequest x)
    {
        var errors = new Dictionary<string, string[]>();
        var name = string.IsNullOrWhiteSpace(x.FullName) ? x.DisplayName : x.FullName;
        if (string.IsNullOrWhiteSpace(x.Email) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(x.Password) || x.Password.Length < 8) errors["user"] = ["Name, email and a password of at least 8 characters are required."];
        var normalized = IdentityRoles.NormalizeAll(x.Roles ?? []);
        if (!IdentityRoles.IsValidAssignment(x.Roles)) errors["roles"] = ["Provide one or more supported roles."];
        if (RiderVehicle.IsRider(normalized))
            foreach (var (key, messages) in RiderVehicle.Validate(x.VehicleType, x.VehicleModel, x.VehicleNumber, x.LicenseNumber)) errors[key] = messages;
        foreach (var (key, messages) in IdentityValidation.Profile(x.IdNumber, x.ContactNumber)) errors[key] = messages;
        return errors;
    }
}
record AdminUserUpdateRequest(string? FullName, string? Email, string? ContactNumber, string? AssignedStore, bool? Active, string? AvailabilityStatus, string[]? Roles = null, string? VehicleType = null, string? VehicleNumber = null, string? LicenseNumber = null, string? District = null, string? Address = null, string? VehicleModel = null)
{
    public bool HasVehicleFields => VehicleType is not null || VehicleModel is not null || VehicleNumber is not null || LicenseNumber is not null;
}
record UserStatusRequest(bool Active);
record UserAvailabilityRequest(string AvailabilityStatus);
record RoleUpdateRequest(string[] Roles);
static class RiderVehicle
{
    private static readonly HashSet<string> VehicleTypes = ["Motorcycle", "Bicycle", "Car", "Van", "Three-Wheeler", "Other"];
    public static bool IsRider(IEnumerable<string> roles) => roles.Any(role => IdentityRoles.Normalize(role) is "Rider");
    public static string NormalizeIdentifier(string? value) => value?.Trim().ToUpperInvariant() ?? "";
    public static Dictionary<string, string[]> Validate(string? vehicleType, string? vehicleModel, string? vehicleNumber, string? licenseNumber)
    {
        var errors = new Dictionary<string, string[]>();
        var type = vehicleType?.Trim() ?? "";
        var model = vehicleModel?.Trim() ?? "";
        var number = NormalizeIdentifier(vehicleNumber);
        var license = NormalizeIdentifier(licenseNumber);
        if (!VehicleTypes.Contains(type)) errors["vehicleType"] = ["Select a valid vehicle type."];
        if (model.Length is 0 or > 100) errors["vehicleModel"] = ["Vehicle model is required and must be 100 characters or fewer."];
        if (!IdentityValidation.VehicleNumber(number)) errors["vehicleNumber"] = ["Please enter a valid vehicle/plate number."];
        if (!IdentityValidation.LicenseNumber(license)) errors["licenseNumber"] = ["Please enter a valid driving license number."];
        return errors;
    }
}
static class IdentityValidation
{
    public static Dictionary<string, string[]> Profile(string? nic, string? phone)
    {
        var errors = new Dictionary<string, string[]>();
        if (!string.IsNullOrWhiteSpace(nic) && !System.Text.RegularExpressions.Regex.IsMatch(nic.Trim(), @"^(?:\d{9}[VvXx]|\d{12})$")) errors["idNumber"] = ["Please enter a valid Sri Lankan NIC number."];
        if (!string.IsNullOrWhiteSpace(phone) && !System.Text.RegularExpressions.Regex.IsMatch(phone.Trim(), @"^(?:0\d{9}|\+94\d{9})$")) errors["contactNumber"] = ["Please enter a valid Sri Lankan phone number."];
        return errors;
    }
    public static bool LicenseNumber(string value) => System.Text.RegularExpressions.Regex.IsMatch(value.Trim(), @"^[A-Z0-9][A-Z0-9-]{3,49}$");
    public static bool VehicleNumber(string value) => System.Text.RegularExpressions.Regex.IsMatch(value.Trim(), @"^[A-Z0-9][A-Z0-9 -]{1,49}$");
}
record User(Guid Id, string Email, string DisplayName, string[] Roles);
record IdentityPrincipal(Guid Id, string[] Roles);
record TokenPayload(Guid Subject, DateTimeOffset ExpiresAt, string[]? Roles);
static class Database
{
    public static async Task InitializeAsync(NpgsqlDataSource db, bool seedDemoData)
    {
        // Ensure new profile columns exist (idempotent — safe on every startup)
        await using (var ensureCols = db.CreateCommand("ALTER TABLE IF EXISTS identity.users ADD COLUMN IF NOT EXISTS full_name text NULL; ALTER TABLE IF EXISTS identity.users ADD COLUMN IF NOT EXISTS id_number text NULL; ALTER TABLE IF EXISTS identity.users ADD COLUMN IF NOT EXISTS contact_number text NULL; ALTER TABLE IF EXISTS identity.users ADD COLUMN IF NOT EXISTS district text NULL; ALTER TABLE IF EXISTS identity.users ADD COLUMN IF NOT EXISTS address text NULL;")) { try { await ensureCols.ExecuteNonQueryAsync(); } catch { /* table may not exist yet on first run — baseline migration will create it */ } }
        await using (var history = db.CreateCommand("CREATE SCHEMA IF NOT EXISTS identity; CREATE TABLE IF NOT EXISTS identity.schema_migrations(version text PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now());")) await history.ExecuteNonQueryAsync();
        await using var conn = await db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        // 001_baseline
        await using (var claim = new NpgsqlCommand("INSERT INTO identity.schema_migrations(version) VALUES('001_baseline') ON CONFLICT DO NOTHING RETURNING version", conn, tx))
        {
            if (await claim.ExecuteScalarAsync() is not null)
            {
                var sql = await MigrationSql.BaselineAsync();
                await using var cmd = new NpgsqlCommand(sql, conn, tx);
                await cmd.ExecuteNonQueryAsync();
                if (seedDemoData) foreach (var user in new[] {
                    new User(Guid.Parse("11111111-1111-1111-1111-111111111111"), "admin@marketflow.local", "Marketflow Admin", ["Admin", "Staff"]),
                    new User(Guid.Parse("22222222-2222-2222-2222-222222222222"), "customer@marketflow.local", "Demo Customer", ["Customer"]),
                    new User(Guid.Parse("33333333-3333-3333-3333-333333333333"), "staff@marketflow.local", "Sam Staff", ["Staff"]),
                    new User(Guid.Parse("44444444-4444-4444-4444-444444444444"), "driver@marketflow.local", "Dave Driver", ["Rider"])
                }) { await using var seed = new NpgsqlCommand("INSERT INTO identity.users(id,email,display_name,password_hash,roles) VALUES ($1,$2,$3,$4,$5) ON CONFLICT(email) DO NOTHING", conn, tx); seed.Parameters.AddWithValue(user.Id); seed.Parameters.AddWithValue(user.Email); seed.Parameters.AddWithValue(user.DisplayName); seed.Parameters.AddWithValue(Password.Hash("ChangeMe!123")); seed.Parameters.AddWithValue(user.Roles); await seed.ExecuteNonQueryAsync(); }
            }
        }

        // 002_unify_staff_roles
        await using (var claim2 = new NpgsqlCommand("INSERT INTO identity.schema_migrations(version) VALUES('002_unify_staff_roles') ON CONFLICT DO NOTHING RETURNING version", conn, tx))
        {
            if (await claim2.ExecuteScalarAsync() is not null)
            {
                var sql2 = await MigrationSql.UnifyStaffRolesAsync();
                await using var cmd2 = new NpgsqlCommand(sql2, conn, tx);
                await cmd2.ExecuteNonQueryAsync();
            }
        }

        // 003_staff_rider_fields
        await using (var claim3 = new NpgsqlCommand("INSERT INTO identity.schema_migrations(version) VALUES('003_staff_rider_fields') ON CONFLICT DO NOTHING RETURNING version", conn, tx))
        {
            if (await claim3.ExecuteScalarAsync() is not null)
            {
                var sql3 = await MigrationSql.StaffRiderFieldsAsync();
                await using var cmd3 = new NpgsqlCommand(sql3, conn, tx);
                await cmd3.ExecuteNonQueryAsync();
            }
        }

        // 004_rider_profiles
        await using (var claim4 = new NpgsqlCommand("INSERT INTO identity.schema_migrations(version) VALUES('004_rider_profiles') ON CONFLICT DO NOTHING RETURNING version", conn, tx))
        {
            if (await claim4.ExecuteScalarAsync() is not null)
            {
                var sql4 = await MigrationSql.RiderProfilesAsync();
                await using var cmd4 = new NpgsqlCommand(sql4, conn, tx);
                await cmd4.ExecuteNonQueryAsync();
            }
        }

        // Each SQL file is claimed and applied in the same transaction.
        foreach (var version in new[] { "005_rider_vehicle_model", "006_standardize_four_roles", "007_enforce_four_roles" })
        {
            await using var claim = new NpgsqlCommand("INSERT INTO identity.schema_migrations(version) VALUES($1) ON CONFLICT DO NOTHING RETURNING version", conn, tx);
            claim.Parameters.AddWithValue(version);
            if (await claim.ExecuteScalarAsync() is not null)
            {
                var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Migrations", version + ".sql"));
                await using var migration = new NpgsqlCommand(sql, conn, tx);
                await migration.ExecuteNonQueryAsync();
            }
        }

        await using (var profileColumns = new NpgsqlCommand("ALTER TABLE identity.users ADD COLUMN IF NOT EXISTS full_name text NULL, ADD COLUMN IF NOT EXISTS id_number text NULL, ADD COLUMN IF NOT EXISTS contact_number text NULL", conn, tx))
            await profileColumns.ExecuteNonQueryAsync();

        await tx.CommitAsync();
    }
}
sealed class TokenService(IConfiguration config)
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(config["Auth:SigningKey"] is { Length: >= 32 } key ? key : throw new InvalidOperationException("Auth:SigningKey must be supplied through configuration and contain at least 32 characters."));
    // The role claim is deliberately named "role" and is consumed by TryRead below.
    public string Create(User u) { var h = ToUrl(Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"))); var p = ToUrl(Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { sub = u.Id, exp = DateTimeOffset.UtcNow.AddHours(8).ToUnixTimeSeconds(), role = IdentityRoles.NormalizeAll(u.Roles) })))); var signed = $"{h}.{p}"; return $"{signed}.{ToUrl(Convert.ToBase64String(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(signed))))}"; }
    public bool TryRead(string token, out TokenPayload payload) { payload = default!; var x = token.Split('.'); if (x.Length is not 2 and not 3) return false; try { var signed = x.Length == 3 ? $"{x[0]}.{x[1]}" : x[0]; var signature = x[^1]; var actual = Convert.FromBase64String(FromUrl(signature)); var expected = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(signed)); if (!CryptographicOperations.FixedTimeEquals(actual, expected)) return false; var json = x.Length == 3 ? x[1] : x[0]; var d = JsonDocument.Parse(Convert.FromBase64String(FromUrl(json))).RootElement; string[]? roles = null; if (d.TryGetProperty("role", out var role) && role.ValueKind == JsonValueKind.Array) roles = IdentityRoles.NormalizeAll(role.EnumerateArray().Select(r => r.GetString() ?? "")); var p = new TokenPayload(d.GetProperty("sub").GetGuid(), DateTimeOffset.FromUnixTimeSeconds(d.GetProperty("exp").GetInt64()), roles); if (p.ExpiresAt <= DateTimeOffset.UtcNow) return false; payload = p; return true; } catch { return false; } }
    static string ToUrl(string s)=>s.TrimEnd('=').Replace('+','-').Replace('/','_'); static string FromUrl(string s)=>s.Replace('-','+').Replace('_','/')+new string('=',(4-s.Length%4)%4); public static string? GetBearer(HttpRequest r)=>r.Headers.Authorization.FirstOrDefault()?.Split(' ',2) is ["Bearer",var t]?t:null; public static string Hash(string t)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(t)));
}
static class Password { public static string Hash(string p) { var s=RandomNumberGenerator.GetBytes(16); var h=Rfc2898DeriveBytes.Pbkdf2(p,s,210000,HashAlgorithmName.SHA512,32); return $"{Convert.ToBase64String(s)}:{Convert.ToBase64String(h)}"; } public static bool Verify(string p,string stored) { var a=stored.Split(':'); if(a.Length!=2)return false; return CryptographicOperations.FixedTimeEquals(Rfc2898DeriveBytes.Pbkdf2(p,Convert.FromBase64String(a[0]),210000,HashAlgorithmName.SHA512,32),Convert.FromBase64String(a[1])); } }
static class IdentityAuth
{
    public static async Task<bool> IsAdmin(HttpRequest request, NpgsqlDataSource db, TokenService tokens) => (await Principal(request, db, tokens))?.Roles.Any(r => IdentityRoles.Normalize(r) == "Admin") == true;
    public static async Task<IdentityPrincipal?> Principal(HttpRequest request, NpgsqlDataSource db, TokenService tokens)
    {
        var token = TokenService.GetBearer(request); if (token is null || !tokens.TryRead(token, out var payload)) return null;
        await using var cmd = db.CreateCommand("SELECT u.roles FROM sessions s JOIN users u ON u.id=s.user_id WHERE s.token_hash=$1 AND s.revoked_at IS NULL AND s.expires_at>now() AND u.active=true"); cmd.Parameters.AddWithValue(TokenService.Hash(token));
        var databaseRoles = await cmd.ExecuteScalarAsync() as string[]; if (databaseRoles is null) return null;
        // Keep existing JWT and legacy sessions valid; current database roles govern access immediately.
        return new IdentityPrincipal(payload.Subject, IdentityRoles.NormalizeAll(databaseRoles));
    }
}
sealed class RequestMetrics { long _requests; long _errors; long _durationTicks; public void Record(int status, TimeSpan duration) { Interlocked.Increment(ref _requests); if (status >= 500) Interlocked.Increment(ref _errors); Interlocked.Add(ref _durationTicks, duration.Ticks); } public string AsPrometheus(string service) => $"# TYPE marketflow_http_requests_total counter\nmarketflow_http_requests_total{{service=\"{service}\"}} {_requests}\n# TYPE marketflow_http_errors_total counter\nmarketflow_http_errors_total{{service=\"{service}\"}} {_errors}\n# TYPE marketflow_http_request_duration_seconds summary\nmarketflow_http_request_duration_seconds_sum{{service=\"{service}\"}} {TimeSpan.FromTicks(_durationTicks).TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}\nmarketflow_http_request_duration_seconds_count{{service=\"{service}\"}} {_requests}\n"; }
static class MigrationSql
{
    public static Task<string> BaselineAsync() => File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Migrations", "001_baseline.sql"));
    public static Task<string> UnifyStaffRolesAsync() => File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Migrations", "002_unify_staff_roles.sql"));
    public static Task<string> StaffRiderFieldsAsync() => File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Migrations", "003_staff_rider_fields.sql"));
    public static Task<string> RiderProfilesAsync() => File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Migrations", "004_rider_profiles.sql"));
    public static Task<string> RiderVehicleModelAsync() => File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Migrations", "005_rider_vehicle_model.sql"));
    public static Task<string> StandardizeFourRolesAsync() => File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Migrations", "006_standardize_four_roles.sql"));
}
static class OpenApi
{
    public record Route(string Method, string Path, string Summary);
    public static string Document(string title, params Route[] routes) => JsonSerializer.Serialize(new { openapi = "3.0.3", info = new { title, version = "1.0.0" }, paths = routes.GroupBy(route => route.Path).ToDictionary(group => group.Key, group => group.ToDictionary(route => route.Method, route => new { summary = route.Summary, responses = new Dictionary<string, object> { ["200"] = new { description = "Successful response" } } })) });
    public const string Ui = """<!doctype html><html><head><title>MarketFlow API</title><link rel="stylesheet" href="https://unpkg.com/swagger-ui-dist@5/swagger-ui.css"></head><body><div id="swagger-ui"></div><script src="https://unpkg.com/swagger-ui-dist@5/swagger-ui-bundle.js"></script><script>SwaggerUIBundle({url:'openapi/v1.json',dom_id:'#swagger-ui'});</script></body></html>""";
}
