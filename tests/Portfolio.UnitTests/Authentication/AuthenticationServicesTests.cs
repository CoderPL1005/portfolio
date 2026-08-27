using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Portfolio.Infrastructure.Authentication;

namespace Portfolio.UnitTests.Authentication;

public sealed class AuthenticationServicesTests
{
    private const string Secret = "test-signing-key-with-at-least-32-characters";
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Password_hash_is_not_plaintext_and_verifies_only_correct_password()
    {
        var hasher = new PasswordHasher();
        var hash = hasher.Hash("correct-password");

        Assert.NotEqual("correct-password", hash);
        Assert.True(hasher.Verify(hash, "correct-password"));
        Assert.False(hasher.Verify(hash, "wrong-password"));
    }

    [Fact]
    public void Access_token_contains_required_claims_and_configured_metadata()
    {
        var adminId = Guid.NewGuid();
        var service = CreateJwtService();
        var result = service.CreateAccessToken(adminId, "admin@example.com");
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Value);

        Assert.Equal("Portfolio.Tests", token.Issuer);
        Assert.Contains("Portfolio.Admin.Tests", token.Audiences);
        Assert.Equal(adminId.ToString(), token.Claims.Single(claim => claim.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal("admin@example.com", token.Claims.Single(claim => claim.Type == JwtRegisteredClaimNames.Email).Value);
        Assert.False(string.IsNullOrWhiteSpace(token.Claims.Single(claim => claim.Type == JwtRegisteredClaimNames.Jti).Value));
        Assert.Equal(900, result.ExpiresInSeconds);
        Assert.Equal(Now.AddMinutes(15), result.ExpiresAt);
    }

    [Fact]
    public void Access_token_signature_issuer_audience_and_lifetime_validate()
    {
        var service = CreateJwtService();
        var token = service.CreateAccessToken(Guid.NewGuid(), "admin@example.com");
        var parameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
            ValidateIssuer = true,
            ValidIssuer = "Portfolio.Tests",
            ValidateAudience = true,
            ValidAudience = "Portfolio.Admin.Tests",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            LifetimeValidator = (notBefore, expires, _, _) =>
                notBefore <= Now.UtcDateTime && expires >= Now.UtcDateTime
        };

        var principal = new JwtSecurityTokenHandler().ValidateToken(token.Value, parameters, out var validated);

        Assert.NotNull(principal);
        Assert.Equal(SecurityAlgorithms.HmacSha256, ((JwtSecurityToken)validated).Header.Alg);
    }

    [Fact]
    public void Refresh_tokens_are_random_opaque_and_only_hash_deterministically()
    {
        var service = CreateJwtService();
        var first = service.CreateRefreshToken();
        var second = service.CreateRefreshToken();

        Assert.NotEqual(first.Value, second.Value);
        Assert.True(first.Value.Length >= 80);
        Assert.Equal(64, service.HashRefreshToken(first.Value).Length);
        Assert.Equal(service.HashRefreshToken(first.Value), service.HashRefreshToken(first.Value));
        Assert.NotEqual(first.Value, service.HashRefreshToken(first.Value));
    }

    private static JwtTokenService CreateJwtService() => new(
        Options.Create(new JwtSettings
        {
            Issuer = "Portfolio.Tests",
            Audience = "Portfolio.Admin.Tests",
            SecretKey = Secret,
            AccessTokenMinutes = 15,
            RefreshTokenDays = 7
        }),
        new FixedTimeProvider(Now));
}
