using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Security.Claims;
using System.Text;
using TNT.ProductService.Api.Data;
using TNT.ProductService.Api.Services;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, services, cfg) =>
        cfg.ReadFrom.Configuration(ctx.Configuration)
           .Enrich.FromLogContext()
           .WriteTo.Console());

    builder.Services.AddDbContext<ProductDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("ProductDb")));

    var jwtSection = builder.Configuration.GetRequiredSection("Jwt");
    var secretKey = jwtSection["SecretKey"];
    if (string.IsNullOrWhiteSpace(secretKey))
    {
        secretKey = "REPLACE_WITH_ENV_VAR_OR_USER_SECRETS";
    }

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.IncludeErrorDetails = builder.Environment.IsDevelopment();
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSection["Issuer"],
                ValidAudience = jwtSection["Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
                NameClaimType = ClaimTypes.NameIdentifier,
                RoleClaimType = ClaimTypes.Role,
                ClockSkew = TimeSpan.Zero
            };
        });
    builder.Services.AddAuthorization();

    builder.Services.AddScoped<StoreService>();
    builder.Services.AddScoped<CategoryService>();
    builder.Services.AddScoped<ProductService>();

    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "TNT Product Service",
            Version = "v1",
            Description = "Product-adjacent catalog APIs including Store Management"
        });
        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "Bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Enter: Bearer {your-token}"
        });
        c.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                },
                Array.Empty<string>()
            }
        });
    });
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<ProductDbContext>(name: "product-db");

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowFrontend", policy =>
        {
            var allowedOrigins = builder.Configuration.GetValue<string>("Cors:AllowedOrigin") ?? "http://localhost:5173";
            var origins = allowedOrigins.Split(",", StringSplitOptions.RemoveEmptyEntries)
                .Select(origin => origin.Trim())
                .ToArray();
            policy
                .WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
    });

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        db.Database.Migrate();
        db.Database.ExecuteSqlRaw(@"
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Categories' AND column_name='UpdatedAt') THEN
                    ALTER TABLE ""Categories"" ALTER COLUMN ""UpdatedAt"" DROP NOT NULL;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Products' AND column_name='Unit') THEN
                    ALTER TABLE ""Products"" ADD COLUMN ""Unit"" character varying(50) NOT NULL DEFAULT 'item';
                END IF;
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Products' AND column_name='ImageUrl') THEN
                    ALTER TABLE ""Products"" ADD COLUMN ""ImageUrl"" character varying(500) NULL;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Products' AND column_name='UpdatedAtUtc') THEN
                    ALTER TABLE ""Products"" ADD COLUMN ""UpdatedAtUtc"" timestamp with time zone NULL;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Products' AND column_name='IsActive') THEN
                    ALTER TABLE ""Products"" ADD COLUMN ""IsActive"" boolean NOT NULL DEFAULT TRUE;
                END IF;
            END $$;
        ");
    }

    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseSerilogRequestLogging();
    app.UseCors("AllowFrontend");
    app.UseStaticFiles();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();
    app.MapHealthChecks("/health");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "TNT.ProductService.Api failed to start");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program { }
