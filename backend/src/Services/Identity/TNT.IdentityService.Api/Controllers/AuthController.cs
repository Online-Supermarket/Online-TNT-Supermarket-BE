using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TNT.IdentityService.Api.DTOs;
using TNT.IdentityService.Api.Exceptions;
using TNT.IdentityService.Api.Interfaces;

namespace TNT.IdentityService.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;
    private readonly IValidator<RegisterRequest> _registerValidator;
    private readonly IValidator<LoginRequest> _loginValidator;

    public AuthController(
        IAuthService authService,
        ILogger<AuthController> logger,
        IValidator<RegisterRequest> registerValidator,
        IValidator<LoginRequest> loginValidator)
    {
        _authService = authService;
        _logger = logger;
        _registerValidator = registerValidator;
        _loginValidator = loginValidator;
    }

    /// <summary>Register a new Buyer account.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(UserSummaryResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        var validation = await _registerValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return BadRequest(new ErrorResponse
            {
                Status = 400,
                Title = "Validation failed",
                Errors = validation.Errors.Select(e => e.ErrorMessage).ToList()
            });
        }

        var result = await _authService.RegisterAsync(request, ct);
        return CreatedAtAction(nameof(Me), null, result);
    }

    /// <summary>Log in and receive JWT + refresh token.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var validation = await _loginValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return Unauthorized(new ErrorResponse
            {
                Status = 401,
                Title = "Unauthorized",
                Detail = "Invalid credentials."
            });
        }

        var ipAddress = GetClientIp();
        var result = await _authService.LoginAsync(request, ipAddress, ct);
        return Ok(result);
    }

    /// <summary>Exchange a refresh token for a new access token (token rotation).</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Unauthorized(new ErrorResponse
            {
                Status = 401,
                Title = "Unauthorized",
                Detail = "Refresh token is required."
            });
        }

        var ipAddress = GetClientIp();
        var result = await _authService.RefreshTokenAsync(request.RefreshToken, ipAddress, ct);
        return Ok(result);
    }

    /// <summary>Revoke a refresh token (logout). Idempotent.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request, CancellationToken ct)
    {
        await _authService.LogoutAsync(request.RefreshToken, ct);
        return NoContent();
    }

    /// <summary>Return current user's profile from JWT claims.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var result = await _authService.GetCurrentUserAsync(userId, ct);
        return Ok(result);
    }

    private string? GetClientIp() =>
        HttpContext.Connection.RemoteIpAddress?.ToString();

    private Guid GetCurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue("sub")
               ?? User.FindFirstValue("uid");

        if (sub == null || !Guid.TryParse(sub, out var userId))
            throw new UnauthorizedException("Unable to determine current user from token.");

        return userId;
    }

    [HttpGet("debug-user")]
    [AllowAnonymous]
    public async Task<IActionResult> DebugUser([FromQuery] string email, [FromServices] TNT.IdentityService.Api.Data.IdentityDbContext db)
    {
        var user = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(db.ApplicationUsers, u => u.Email == email);
        if (user == null) return NotFound("User not found");
        return Ok(new { user.Email, user.NormalizedEmail, user.IsActive, user.PasswordHash, user.Role });
    }
}
