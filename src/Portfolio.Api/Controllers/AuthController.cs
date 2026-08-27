using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Portfolio.Api.Contracts.Common;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Features.Auth;
using Portfolio.Application.Features.Auth.GetCurrentAdmin;
using Portfolio.Application.Features.Auth.Login;
using Portfolio.Application.Features.Auth.Logout;
using Portfolio.Application.Features.Auth.Refresh;

namespace Portfolio.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(IRequestDispatcher dispatcher) : ControllerBase
{
    private const string RefreshCookieName = "portfolio_refresh_token";

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    public async Task<ActionResult<ApiResponse<LoginResponse>>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.DispatchAsync(
            new LoginCommand(request.Email, request.Password), cancellationToken);
        SetRefreshCookie(result.RefreshToken, result.RefreshTokenExpiresAt);
        return Ok(ApiResponse<LoginResponse>.Ok(new LoginResponse(
            result.AccessToken,
            result.ExpiresIn,
            new AdminResponse(result.Admin.Id, result.Admin.Email, result.Admin.FullName))));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<TokenResponse>>> Refresh(CancellationToken cancellationToken)
    {
        var rawToken = Request.Cookies[RefreshCookieName] ?? string.Empty;
        var result = await dispatcher.DispatchAsync(new RefreshCommand(rawToken), cancellationToken);
        SetRefreshCookie(result.RefreshToken, result.RefreshTokenExpiresAt);
        return Ok(ApiResponse<TokenResponse>.Ok(new TokenResponse(result.AccessToken, result.ExpiresIn)));
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var rawToken = Request.Cookies[RefreshCookieName] ?? string.Empty;
        await dispatcher.DispatchAsync(new LogoutCommand(rawToken), cancellationToken);
        Response.Cookies.Delete(RefreshCookieName, RefreshCookieOptions());
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<CurrentAdminResponse>>> Me(CancellationToken cancellationToken)
    {
        var result = await dispatcher.DispatchAsync(new GetCurrentAdminQuery(), cancellationToken);
        return Ok(ApiResponse<CurrentAdminResponse>.Ok(new CurrentAdminResponse(
            result.Id, result.Email, result.FullName, result.LastLoginAt)));
    }

    private void SetRefreshCookie(string token, DateTimeOffset expiresAt)
    {
        var options = RefreshCookieOptions();
        options.Expires = expiresAt;
        Response.Cookies.Append(RefreshCookieName, token, options);
    }

    private static CookieOptions RefreshCookieOptions() => new()
    {
        HttpOnly = true,
        Secure = true,
        // The frozen deployment architecture hosts Angular and the API on separate sites.
        // None is required for the refresh cookie on cross-site credentialed requests.
        SameSite = SameSiteMode.None,
        Path = "/api/v1/auth",
        IsEssential = true
    };
}

public sealed record LoginRequest(string Email, string Password);
public sealed record AdminResponse(Guid Id, string Email, string? FullName);
public sealed record LoginResponse(string AccessToken, int ExpiresIn, AdminResponse Admin);
public sealed record TokenResponse(string AccessToken, int ExpiresIn);
public sealed record CurrentAdminResponse(Guid Id, string Email, string? FullName, DateTimeOffset? LastLoginAt);
