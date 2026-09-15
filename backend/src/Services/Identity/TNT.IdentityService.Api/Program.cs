using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Security.Claims;
using System.Text;
using TNT.IdentityService.Api.Data;
using TNT.IdentityService.Api.DTOs;
using TNT.IdentityService.Api.Interfaces;
using TNT.IdentityService.Api.Kafka;
using TNT.IdentityService.Api.Middleware;
using TNT.IdentityService.Api.Services;

// ──────────────────────────────────────────────────────────────────────────────
// Serilog bootstrap logger (captures startup errors before host is built)
// ──────────────────────────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting TNT.IdentityService.Api");

    var builder = WebApplication.CreateBuilder(args);

    // ──────────────────────────────────────────────────────────────────────────
    // Serilog
    // ──────────────────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((ctx, services, cfg) =>
        cfg.ReadFrom.Configuration(ctx.Configuration)
           .ReadFrom.Services(services)
           .Enrich.FromLogContext()
           .WriteTo.Console());

    // ──────────────────────────────────────────────────────────────────────────
    // Configuration binding
    // ──────────────────────────────────────────────────────────────────────────
    builder.Services.AddOptions<JwtSettings>()
        .Bind(builder.Configuration.GetSection(JwtSettings.SectionName))
        .Validate(settings => !string.IsNullOrWhiteSpace(settings.Issuer),
            "Jwt:Issuer is required.")
        .Validate(settings => !string.IsNullOrWhiteSpace(settings.Audience),
            "Jwt:Audience is required.")
        .Validate(settings => !string.IsNullOrWhiteSpace(settings.SecretKey)
                              && settings.SecretKey.Length >= 32,
            "Jwt:SecretKey must be at least 32 characters long.")
        .Validate(settings => settings.AccessTokenMinutes > 0,
            "Jwt:AccessTokenMinutes must be greater than zero.")
        .Validate(settings => settings.RefreshTokenDays > 0,
            "Jwt:RefreshTokenDays must be greater than zero.")
        .ValidateOnStart();
    builder.Services.Configure<KafkaSettings>(
        builder.Configuration.GetSection(KafkaSettings.SectionName));

    // ──────────────────────────────────────────────────────────────────────────
    // Database — PostgreSQL via EF Core
    // ──────────────────────────────────────────────────────────────────────────
    var identityConnectionString = builder.Configuration.GetConnectionString("IdentityDb");
    if (string.IsNullOrWhiteSpace(identityConnectionString))
    {
        throw new InvalidOperationException("ConnectionStrings:IdentityDb is required.");
    }

    builder.Services.AddDbContext<IdentityDbContext>(opts =>
        opts.UseNpgsql(identityConnectionString, postgres =>
        {
            postgres.CommandTimeout(30);
            postgres.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorCodesToAdd: null);
        }));

    // ──────────────────────────────────────────────────────────────────────────
    // JWT Authentication
    // ──────────────────────────────────────────────────────────────────────────
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer();

    builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
        .Configure<Microsoft.Extensions.Options.IOptions<JwtSettings>>((opts, jwtOptions) =>
        {
            opts.IncludeErrorDetails = builder.Environment.IsDevelopment();
            var settings = jwtOptions.Value;
            opts.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = settings.Issuer,
                ValidAudience = settings.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(settings.SecretKey)),
                NameClaimType = ClaimTypes.NameIdentifier,
                RoleClaimType = ClaimTypes.Role,
                ClockSkew = TimeSpan.Zero
            };
        });

    builder.Services.AddAuthorization();

    // ──────────────────────────────────────────────────────────────────────────
    // Application services
    // ──────────────────────────────────────────────────────────────────────────
    builder.Services.AddScoped<IAuthService, AuthService>();
    builder.Services.AddScoped<ITokenService, TokenService>();

    // Kafka: register real or no-op producer based on configuration
    var kafkaEnabled = builder.Configuration.GetValue<bool>("Kafka:Enabled", defaultValue: false);
    if (kafkaEnabled)
    {
        builder.Services.AddSingleton<IKafkaProducerService, KafkaProducerService>();
    }
    else
    {
        builder.Services.AddSingleton<IKafkaProducerService, NoOpKafkaProducerService>();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // FluentValidation
    // ──────────────────────────────────────────────────────────────────────────
    builder.Services.AddFluentValidationAutoValidation();
    builder.Services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();

    // ──────────────────────────────────────────────────────────────────────────
    // Controllers
    // ──────────────────────────────────────────────────────────────────────────
    builder.Services.AddControllers();

    // ──────────────────────────────────────────────────────────────────────────
    // Swagger / OpenAPI
    // ──────────────────────────────────────────────────────────────────────────
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "TNT Identity Service",
            Version = "v1",
            Description = "Authentication and identity management for TNT Supermarket"
        });

        // JWT Bearer security definition
        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "Bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Enter your JWT token. Example: Bearer {your-token}"
        });

        c.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });

        // Include XML comments if available
        var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath))
        {
            c.IncludeXmlComments(xmlPath);
        }
    });

    // ──────────────────────────────────────────────────────────────────────────
    // Health checks
    // ──────────────────────────────────────────────────────────────────────────
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<IdentityDbContext>(
            name: "identity-db",
            tags: ["ready"]);

    // ──────────────────────────────────────────────────────────────────────────
    // CORS (allow frontend during local dev)
    // ──────────────────────────────────────────────────────────────────────────
    builder.Services.AddCors(opts =>
    {
        opts.AddPolicy("AllowFrontend", policy =>
        {
            var allowedOrigins = builder.Configuration.GetValue<string>("Cors:AllowedOrigin") ?? "http://localhost:5173";
            var origins = allowedOrigins.Split(",", StringSplitOptions.RemoveEmptyEntries)
                .Select(o => o.Trim())
                .ToArray();
            policy
                .WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
    });

    // ──────────────────────────────────────────────────────────────────────────
    // Build app
    // ──────────────────────────────────────────────────────────────────────────
    var app = builder.Build();

    // ──────────────────────────────────────────────────────────────────────────
    // Middleware pipeline
    // ──────────────────────────────────────────────────────────────────────────
    app.UseMiddleware<ExceptionHandlingMiddleware>();

    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "TNT Identity Service v1");
        c.RoutePrefix = string.Empty; // Serve swagger at root
    });

    app.UseSerilogRequestLogging();
    app.UseCors("AllowFrontend");
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();

    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = _ => false
    });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready")
    });
    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready")
    });

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "TNT.IdentityService.Api failed to start");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

// Make Program accessible for WebApplicationFactory in integration tests
public partial class Program { }
