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
await Database.InitializeAsync(app.Services.GetRequiredService<NpgsqlDataSource>());
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "identity" }));
app.MapGet("/health/ready", async (NpgsqlDataSource db) =>
{
    try { await using var cmd = db.CreateCommand("SELECT 1"); await cmd.ExecuteScalarAsync(); return Results.Ok(new { status = "ready", service = "identity", dependencies = new { postgres = "ready" } }); }
    catch { return Results.Problem("PostgreSQL is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable); }
});
app.MapGet("/metrics", (RequestMetrics m) => Results.Text(m.AsPrometheus("identity"), "text/plain"));
app.MapGet("/openapi/v1.json", () => Results.Text(OpenApi.Document("MarketFlow Identity API", new("get", "/health", "Liveness check"), new("get", "/health/ready", "Dependency readiness check"), new("post", "/auth/login", "Sign in"), new("post", "/auth/register", "Register a customer"), new("post", "/auth/logout", "Revoke the current session"), new("get", "/auth/introspect", "Inspect the current token"), new("get", "/users/me", "Read the current account"), new("get", "/admin/users", "List accounts"), new("post", "/admin/users", "Create a staff account")), "application/json"));
app.MapGet("/swagger", () => Results.Content(OpenApi.Ui, "text/html"));

app.MapPost("/auth/login", async (LoginRequest request, NpgsqlDataSource db, TokenService tokens) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["credentials"] = ["Email and password are required."] });
    await using var cmd = db.CreateCommand("SELECT id,email,display_name,password_hash,roles,active FROM users WHERE lower(email)=lower($1)"); cmd.Parameters.AddWithValue(request.Email.Trim());
    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync() || !reader.GetBoolean(5) || !Password.Verify(request.Password, reader.GetString(3))) return Results.Unauthorized();
    var user = new User(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<string[]>(4)); var token = tokens.Create(user); await reader.CloseAsync();
    await using var session = db.CreateCommand("INSERT INTO sessions(token_hash,user_id,expires_at) VALUES ($1,$2,$3)"); session.Parameters.AddWithValue(TokenService.Hash(token)); session.Parameters.AddWithValue(user.Id); session.Parameters.AddWithValue(DateTimeOffset.UtcNow.AddHours(8)); await session.ExecuteNonQueryAsync();
    return Results.Ok(new { accessToken = token, tokenType = "Bearer", expiresInSeconds = 28800, user = new { user.Id, user.Email, user.DisplayName, roles = user.Roles } });
});
app.MapPost("/auth/register", async (RegisterRequest request, NpgsqlDataSource db, TokenService tokens) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.DisplayName) || string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8) return Results.ValidationProblem(new Dictionary<string, string[]> { ["registration"] = ["Name, email and a password of at least 8 characters are required."] });
    var user = new User(Guid.NewGuid(), request.Email.Trim(), request.DisplayName.Trim(), ["Customer"]);
    try { await using var cmd = db.CreateCommand("INSERT INTO users(id,email,display_name,password_hash,roles) VALUES($1,$2,$3,$4,$5)"); cmd.Parameters.AddWithValue(user.Id); cmd.Parameters.AddWithValue(user.Email); cmd.Parameters.AddWithValue(user.DisplayName); cmd.Parameters.AddWithValue(Password.Hash(request.Password)); cmd.Parameters.AddWithValue(user.Roles); await cmd.ExecuteNonQueryAsync(); var token = tokens.Create(user); await using var session = db.CreateCommand("INSERT INTO sessions(token_hash,user_id,expires_at) VALUES($1,$2,$3)"); session.Parameters.AddWithValue(TokenService.Hash(token)); session.Parameters.AddWithValue(user.Id); session.Parameters.AddWithValue(DateTimeOffset.UtcNow.AddHours(8)); await session.ExecuteNonQueryAsync(); return Results.Created("/users/me", new { accessToken = token, tokenType = "Bearer", expiresInSeconds = 28800, user = new { user.Id, user.Email, user.DisplayName, roles = user.Roles } }); }
    catch (PostgresException e) when (e.SqlState == "23505") { return Results.Conflict(new { message = "An account with that email already exists." }); }
});
app.MapPost("/auth/logout", async (HttpRequest request, NpgsqlDataSource db) => { var token = TokenService.GetBearer(request); if (token is null) return Results.Unauthorized(); await using var cmd = db.CreateCommand("UPDATE sessions SET revoked_at=now() WHERE token_hash=$1 AND revoked_at IS NULL"); cmd.Parameters.AddWithValue(TokenService.Hash(token)); await cmd.ExecuteNonQueryAsync(); return Results.NoContent(); });
app.MapGet("/auth/introspect", async (HttpRequest request, NpgsqlDataSource db, TokenService tokens) =>
{
    var token = TokenService.GetBearer(request); if (token is null || !tokens.TryRead(token, out var payload)) return Results.Ok(new { active = false });
    await using var cmd = db.CreateCommand("SELECT u.email,u.display_name,u.roles FROM sessions s JOIN users u ON u.id=s.user_id WHERE s.token_hash=$1 AND s.revoked_at IS NULL AND s.expires_at>now() AND u.active=true"); cmd.Parameters.AddWithValue(TokenService.Hash(token)); await using var reader = await cmd.ExecuteReaderAsync();
    return await reader.ReadAsync() ? Results.Ok(new { active = true, subject = payload.Subject, email = reader.GetString(0), displayName = reader.GetString(1), roles = reader.GetFieldValue<string[]>(2), expiresAt = payload.ExpiresAt }) : Results.Ok(new { active = false });
});
app.MapGet("/users/me", async (HttpRequest request, NpgsqlDataSource db, TokenService tokens) => { var token = TokenService.GetBearer(request); if (token is null || !tokens.TryRead(token, out var payload)) return Results.Unauthorized(); await using var cmd = db.CreateCommand("SELECT email,display_name,roles FROM users WHERE id=$1 AND active=true"); cmd.Parameters.AddWithValue(payload.Subject); await using var r = await cmd.ExecuteReaderAsync(); return await r.ReadAsync() ? Results.Ok(new { id = payload.Subject, email = r.GetString(0), displayName = r.GetString(1), roles = r.GetFieldValue<string[]>(2) }) : Results.Unauthorized(); });
app.MapGet("/admin/users", async (HttpRequest request, NpgsqlDataSource db, TokenService tokens) =>
{
    if (!await IdentityAuth.IsAdmin(request, db, tokens)) return Results.Forbid();
    await using var cmd = db.CreateCommand("SELECT id,email,display_name,roles,active,created_at FROM users ORDER BY created_at DESC");
    await using var reader = await cmd.ExecuteReaderAsync(); var rows = new List<object>();
    while (await reader.ReadAsync()) rows.Add(new { id = reader.GetGuid(0), email = reader.GetString(1), displayName = reader.GetString(2), roles = reader.GetFieldValue<string[]>(3), active = reader.GetBoolean(4), createdAt = reader.GetFieldValue<DateTimeOffset>(5) });
    return Results.Ok(rows);
});
app.MapPost("/admin/users", async (AdminUserRequest request, HttpRequest http, NpgsqlDataSource db, TokenService tokens) =>
{
    if (!await IdentityAuth.IsAdmin(http, db, tokens)) return Results.Forbid();
    var errors = AdminUserRequest.Validate(request); if (errors.Count > 0) return Results.ValidationProblem(errors);
    var user = new User(Guid.NewGuid(), request.Email.Trim(), request.DisplayName.Trim(), request.Roles.Distinct(StringComparer.Ordinal).ToArray());
    try
    {
        await using var cmd = db.CreateCommand("INSERT INTO users(id,email,display_name,password_hash,roles) VALUES($1,$2,$3,$4,$5)");
        cmd.Parameters.AddWithValue(user.Id); cmd.Parameters.AddWithValue(user.Email); cmd.Parameters.AddWithValue(user.DisplayName); cmd.Parameters.AddWithValue(Password.Hash(request.Password)); cmd.Parameters.AddWithValue(user.Roles);
        await cmd.ExecuteNonQueryAsync(); return Results.Created($"/admin/users/{user.Id}", new { id = user.Id });
    }
    catch (PostgresException e) when (e.SqlState == "23505") { return Results.Conflict(new { message = "An account with that email already exists." }); }
});
app.MapPut("/admin/users/{id:guid}/roles", async (Guid id, RoleUpdateRequest request, HttpRequest http, NpgsqlDataSource db, TokenService tokens) =>
{
    var admin = await IdentityAuth.Principal(http, db, tokens); if (admin is null || !admin.Roles.Contains("OperationsAdmin")) return Results.Forbid();
    var roles = request.Roles.Distinct(StringComparer.Ordinal).ToArray();
    if (roles.Length == 0 || roles.Any(x => !IdentityRoles.All.Contains(x))) return Results.ValidationProblem(new Dictionary<string, string[]> { ["roles"] = ["Provide one or more supported roles."] });
    if (admin.Id == id && !roles.Contains("OperationsAdmin")) return Results.Conflict(new { message = "An administrator cannot remove their own OperationsAdmin role." });
    await using var cmd = db.CreateCommand("UPDATE users SET roles=$2 WHERE id=$1 AND active=true"); cmd.Parameters.AddWithValue(id); cmd.Parameters.AddWithValue(roles);
    return await cmd.ExecuteNonQueryAsync() == 1 ? Results.NoContent() : Results.NotFound();
});
app.Run();

record LoginRequest(string Email, string Password);
record RegisterRequest(string Email, string DisplayName, string Password);
record AdminUserRequest(string Email, string DisplayName, string Password, string[] Roles)
{
    public static Dictionary<string, string[]> Validate(AdminUserRequest x)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(x.Email) || string.IsNullOrWhiteSpace(x.DisplayName) || string.IsNullOrWhiteSpace(x.Password) || x.Password.Length < 8) errors["user"] = ["Name, email and a password of at least 8 characters are required."];
        if (x.Roles is null || x.Roles.Length == 0 || x.Roles.Any(role => !IdentityRoles.All.Contains(role))) errors["roles"] = ["Provide one or more supported roles."];
        return errors;
    }
}
record RoleUpdateRequest(string[] Roles);
record User(Guid Id, string Email, string DisplayName, string[] Roles);
record IdentityPrincipal(Guid Id, string[] Roles);
record TokenPayload(Guid Subject, DateTimeOffset ExpiresAt);
static class Database
{
    public static async Task InitializeAsync(NpgsqlDataSource db)
    {
        await using (var history = db.CreateCommand("CREATE SCHEMA IF NOT EXISTS identity; CREATE TABLE IF NOT EXISTS identity.schema_migrations(version text PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now());")) await history.ExecuteNonQueryAsync();
        await using var conn = await db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using var claim = new NpgsqlCommand("INSERT INTO identity.schema_migrations(version) VALUES('001_baseline') ON CONFLICT DO NOTHING RETURNING version", conn, tx);
        if (await claim.ExecuteScalarAsync() is null) { await tx.CommitAsync(); return; }
        var sql = await MigrationSql.BaselineAsync();
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        await cmd.ExecuteNonQueryAsync();
        foreach (var user in new[] { new User(Guid.Parse("11111111-1111-1111-1111-111111111111"), "admin@marketflow.local", "Marketflow Admin", ["OperationsAdmin", "CatalogStaff", "InventoryStaff"]), new User(Guid.Parse("22222222-2222-2222-2222-222222222222"), "customer@marketflow.local", "Demo Customer", ["Customer"]) }) { await using var seed = new NpgsqlCommand("INSERT INTO identity.users(id,email,display_name,password_hash,roles) VALUES ($1,$2,$3,$4,$5) ON CONFLICT(email) DO NOTHING", conn, tx); seed.Parameters.AddWithValue(user.Id); seed.Parameters.AddWithValue(user.Email); seed.Parameters.AddWithValue(user.DisplayName); seed.Parameters.AddWithValue(Password.Hash("ChangeMe!123")); seed.Parameters.AddWithValue(user.Roles); await seed.ExecuteNonQueryAsync(); }
        await tx.CommitAsync();
    }
}
sealed class TokenService(IConfiguration config) { private readonly byte[] _key = Encoding.UTF8.GetBytes(config["Auth:SigningKey"] is { Length: >= 32 } key ? key : throw new InvalidOperationException("Auth:SigningKey must be supplied through configuration and contain at least 32 characters.")); public string Create(User u) { var p = ToUrl(Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { sub = u.Id, exp = DateTimeOffset.UtcNow.AddHours(8).ToUnixTimeSeconds() })))); return $"{p}.{ToUrl(Convert.ToBase64String(HMACSHA256.HashData(_key,Encoding.UTF8.GetBytes(p))))}"; } public bool TryRead(string token,out TokenPayload payload) { payload=default!; var x=token.Split('.'); if(x.Length!=2)return false; try { var actual=Convert.FromBase64String(FromUrl(x[1])); var expected=HMACSHA256.HashData(_key,Encoding.UTF8.GetBytes(x[0])); if(!CryptographicOperations.FixedTimeEquals(actual,expected))return false; var d=JsonDocument.Parse(Convert.FromBase64String(FromUrl(x[0]))).RootElement; var p=new TokenPayload(d.GetProperty("sub").GetGuid(),DateTimeOffset.FromUnixTimeSeconds(d.GetProperty("exp").GetInt64())); if(p.ExpiresAt<=DateTimeOffset.UtcNow)return false; payload=p; return true; } catch { return false; } } static string ToUrl(string s)=>s.TrimEnd('=').Replace('+','-').Replace('/','_'); static string FromUrl(string s)=>s.Replace('-','+').Replace('_','/')+new string('=',(4-s.Length%4)%4); public static string? GetBearer(HttpRequest r)=>r.Headers.Authorization.FirstOrDefault()?.Split(' ',2) is ["Bearer",var t]?t:null; public static string Hash(string t)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(t))); }
static class Password { public static string Hash(string p) { var s=RandomNumberGenerator.GetBytes(16); var h=Rfc2898DeriveBytes.Pbkdf2(p,s,210000,HashAlgorithmName.SHA512,32); return $"{Convert.ToBase64String(s)}:{Convert.ToBase64String(h)}"; } public static bool Verify(string p,string stored) { var a=stored.Split(':'); if(a.Length!=2)return false; return CryptographicOperations.FixedTimeEquals(Rfc2898DeriveBytes.Pbkdf2(p,Convert.FromBase64String(a[0]),210000,HashAlgorithmName.SHA512,32),Convert.FromBase64String(a[1])); } }
static class IdentityRoles { public static readonly HashSet<string> All = ["Customer", "CatalogStaff", "InventoryStaff", "OperationsAdmin", "Dispatcher", "Courier"]; }
static class IdentityAuth
{
    public static async Task<bool> IsAdmin(HttpRequest request, NpgsqlDataSource db, TokenService tokens) => (await Principal(request, db, tokens))?.Roles.Contains("OperationsAdmin") == true;
    public static async Task<IdentityPrincipal?> Principal(HttpRequest request, NpgsqlDataSource db, TokenService tokens)
    {
        var token = TokenService.GetBearer(request); if (token is null || !tokens.TryRead(token, out var payload)) return null;
        await using var cmd = db.CreateCommand("SELECT u.roles FROM sessions s JOIN users u ON u.id=s.user_id WHERE s.token_hash=$1 AND s.revoked_at IS NULL AND s.expires_at>now() AND u.active=true"); cmd.Parameters.AddWithValue(TokenService.Hash(token));
        var roles = await cmd.ExecuteScalarAsync() as string[]; return roles is null ? null : new IdentityPrincipal(payload.Subject, roles);
    }
}
sealed class RequestMetrics { long _requests; long _errors; long _durationTicks; public void Record(int status, TimeSpan duration) { Interlocked.Increment(ref _requests); if (status >= 500) Interlocked.Increment(ref _errors); Interlocked.Add(ref _durationTicks, duration.Ticks); } public string AsPrometheus(string service) => $"# TYPE marketflow_http_requests_total counter\nmarketflow_http_requests_total{{service=\"{service}\"}} {_requests}\n# TYPE marketflow_http_errors_total counter\nmarketflow_http_errors_total{{service=\"{service}\"}} {_errors}\n# TYPE marketflow_http_request_duration_seconds summary\nmarketflow_http_request_duration_seconds_sum{{service=\"{service}\"}} {TimeSpan.FromTicks(_durationTicks).TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}\nmarketflow_http_request_duration_seconds_count{{service=\"{service}\"}} {_requests}\n"; }
static class MigrationSql { public static Task<string> BaselineAsync() => File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Migrations", "001_baseline.sql")); }
static class OpenApi
{
    public record Route(string Method, string Path, string Summary);
    public static string Document(string title, params Route[] routes) => JsonSerializer.Serialize(new { openapi = "3.0.3", info = new { title, version = "1.0.0" }, paths = routes.GroupBy(route => route.Path).ToDictionary(group => group.Key, group => group.ToDictionary(route => route.Method, route => new { summary = route.Summary, responses = new Dictionary<string, object> { ["200"] = new { description = "Successful response" } } })) });
    public const string Ui = """<!doctype html><html><head><title>MarketFlow API</title><link rel="stylesheet" href="https://unpkg.com/swagger-ui-dist@5/swagger-ui.css"></head><body><div id="swagger-ui"></div><script src="https://unpkg.com/swagger-ui-dist@5/swagger-ui-bundle.js"></script><script>SwaggerUIBundle({url:'openapi/v1.json',dom_id:'#swagger-ui'});</script></body></html>""";
}
