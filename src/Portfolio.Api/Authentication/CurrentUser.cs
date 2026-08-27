using System.IdentityModel.Tokens.Jwt;
using Portfolio.Application.Common.Abstractions.Authentication;

namespace Portfolio.Api.Authentication;

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private System.Security.Claims.ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? AdminUserId => Guid.TryParse(
        Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
        out var id) ? id : null;

    public string? Email => Principal?.FindFirst(JwtRegisteredClaimNames.Email)?.Value;
}
