using Microsoft.EntityFrameworkCore;
using TNT.IdentityService.Api.Data;
using TNT.IdentityService.Api.DTOs;
using TNT.IdentityService.Api.Entities;
using TNT.IdentityService.Api.Enums;
using TNT.IdentityService.Api.Events;
using TNT.IdentityService.Api.Exceptions;
using TNT.IdentityService.Api.Interfaces;
using Microsoft.Extensions.Options;

namespace TNT.IdentityService.Api.Services;

/// <summary>
/// Core authentication service handling registration, login, token refresh, logout, and profile lookup.
/// </summary>
public class AuthService : IAuthService
{
    private readonly IdentityDbContext _db;
    private readonly ITokenService _tokenService;
    private readonly IKafkaProducerService _kafkaProducer;
    private readonly ILogger<AuthService> _logger;
    private readonly JwtSettings _jwtSettings;

    public AuthService(
        IdentityDbContext db,
        ITokenService tokenService,
        IKafkaProducerService kafkaProducer,
        ILogger<AuthService> logger,
        IOptions<JwtSettings> jwtSettings)
    {
        _db = db;
        _tokenService = tokenService;
        _kafkaProducer = kafkaProducer;
        _logger = logger;
        _jwtSettings = jwtSettings.Value;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // REGISTER
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<UserSummaryResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var normalizedEmail = NormalizeEmail(request.Email);

        // Check for duplicate email (case-insensitive via normalised index)
        var exists = await _db.ApplicationUsers
            .AnyAsync(u => u.NormalizedEmail == normalizedEmail, ct);

        if (exists)
        {
            _logger.LogWarning("Registration attempt with duplicate email: {Email}", normalizedEmail);
            throw new ConflictException("An account with this email address already exists.");
        }

        // Public registration → always Buyer
        // Privileged roles are assigned via a protected administrative workflow only.
        var user = new ApplicationUser
        {
            FullName = request.FullName.Trim(),
            Email = request.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12),
            PhoneNumber = request.PhoneNumber?.Trim(),
            Role = UserRole.Buyer.ToString(),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.ApplicationUsers.Add(user);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("User registered. UserId={UserId}, Email={Email}", user.Id, normalizedEmail);

        // Publish Kafka event (fire-and-forget; failures are logged, not thrown)
        var evt = new UserRegisteredEvent
        {
            UserId = user.Id,
            Email = user.Email,
            Role = user.Role
        };
        await _kafkaProducer.PublishUserRegisteredAsync(evt, ct);

        return MapToSummary(user);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // LOGIN
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<AuthResponse> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken ct = default)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        _logger.LogWarning("Attempting to find user with email: {Email}", normalizedEmail);

        var user = await _db.ApplicationUsers
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail && u.IsActive, ct);

        if (user == null) 
        {
            _logger.LogWarning("User NOT FOUND in database for email: {Email}", normalizedEmail);
            throw new UnauthorizedException("Invalid credentials.");
        }
        
        _logger.LogWarning("User FOUND! Id: {UserId}, Role: {Role}, Hash: {Hash}", user.Id, user.Role, user.PasswordHash);

        if (!IsValidPassword(request.Password, user.PasswordHash))
        {
            _logger.LogWarning("Password mismatch for email: {Email}", normalizedEmail);
            throw new UnauthorizedException("Invalid credentials.");
        }

        _logger.LogInformation("Successful login. UserId={UserId}", user.Id);
        return await IssueTokensAsync(user, ipAddress, ct);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // REFRESH TOKEN
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<AuthResponse> RefreshTokenAsync(string rawRefreshToken, string? ipAddress, CancellationToken ct = default)
    {
        var tokenHash = _tokenService.HashToken(rawRefreshToken);

        var existingToken = await _db.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, ct);

        if (existingToken == null || existingToken.IsRevoked || existingToken.IsExpired)
        {
            _logger.LogWarning("Invalid or expired refresh token presented.");
            throw new UnauthorizedException("Invalid or expired refresh token.");
        }

        var user = existingToken.User;
        if (!user.IsActive)
        {
            throw new UnauthorizedException("Account is not active.");
        }

        // Rotate: revoke old token
        existingToken.RevokedAtUtc = DateTime.UtcNow;

        // Issue new refresh token
        var rawNewToken = _tokenService.GenerateRawRefreshToken();
        var newTokenHash = _tokenService.HashToken(rawNewToken);

        existingToken.ReplacedByTokenHash = newTokenHash;

        var newRefreshToken = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = newTokenHash,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenDays),
            CreatedByIp = ipAddress
        };

        _db.RefreshTokens.Add(newRefreshToken);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Refresh token rotated for UserId={UserId}", user.Id);

        var accessToken = _tokenService.GenerateAccessToken(user);
        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = rawNewToken,
            ExpiresIn = _tokenService.GetAccessTokenLifetimeSeconds(),
            User = MapToSummary(user)
        };
    }

    // ──────────────────────────────────────────────────────────────────────────
    // LOGOUT
    // ──────────────────────────────────────────────────────────────────────────

    public async Task LogoutAsync(string rawRefreshToken, CancellationToken ct = default)
    {
        var tokenHash = _tokenService.HashToken(rawRefreshToken);

        var token = await _db.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, ct);

        if (token == null || token.IsRevoked)
        {
            // Idempotent: do not fail if already revoked or not found
            _logger.LogInformation("Logout called with already-revoked or unknown token — no action taken.");
            return;
        }

        token.RevokedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Refresh token revoked for UserId={UserId}", token.UserId);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GET CURRENT USER
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<UserSummaryResponse> GetCurrentUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _db.ApplicationUsers
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct);

        if (user == null)
        {
            throw new NotFoundException("User not found.");
        }

        return MapToSummary(user);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // PRIVATE HELPERS
    // ──────────────────────────────────────────────────────────────────────────

    private async Task<AuthResponse> IssueTokensAsync(ApplicationUser user, string? ipAddress, CancellationToken ct)
    {
        var accessToken = _tokenService.GenerateAccessToken(user);
        var rawRefreshToken = _tokenService.GenerateRawRefreshToken();
        var refreshTokenHash = _tokenService.HashToken(rawRefreshToken);

        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshTokenHash,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenDays),
            CreatedByIp = ipAddress
        };

        _db.RefreshTokens.Add(refreshToken);
        await _db.SaveChangesAsync(ct);

        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = rawRefreshToken,
            ExpiresIn = _tokenService.GetAccessTokenLifetimeSeconds(),
            User = MapToSummary(user)
        };
    }

    private static string NormalizeEmail(string email) =>
        email.Trim().ToUpperInvariant();

    private bool IsValidPassword(string password, string? passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            _logger.LogWarning("Login rejected because the stored password hash is empty.");
            return false;
        }

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }
        catch (Exception ex) when (ex is BCrypt.Net.SaltParseException
                                   || ex is FormatException
                                   || ex is ArgumentException)
        {
            _logger.LogWarning(ex, "Login rejected because the stored password hash is malformed.");
            return false;
        }
    }

    private static UserSummaryResponse MapToSummary(ApplicationUser user) =>
        new()
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            Role = user.Role
        };
}
