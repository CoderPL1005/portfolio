using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Portfolio.Application.Common.Abstractions.Authentication;

namespace Portfolio.Infrastructure.Authentication;

public sealed class JwtTokenService(IOptions<JwtSettings> options, TimeProvider timeProvider)
    : IJwtTokenService
{
    private readonly JwtSettings _settings = options.Value;

    public AccessToken CreateAccessToken(Guid adminUserId, string email)
    {
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(_settings.AccessTokenMinutes);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SecretKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, adminUserId.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            ],
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new AccessToken(
            new JwtSecurityTokenHandler().WriteToken(token),
            checked(_settings.AccessTokenMinutes * 60),
            expiresAt);
    }

    public RefreshToken CreateRefreshToken()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(64);
        return new RefreshToken(
            Base64UrlEncoder.Encode(randomBytes),
            timeProvider.GetUtcNow().AddDays(_settings.RefreshTokenDays));
    }

    public string HashRefreshToken(string rawToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(rawToken);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
    }
}
