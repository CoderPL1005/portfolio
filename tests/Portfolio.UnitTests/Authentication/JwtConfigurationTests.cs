using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Portfolio.Infrastructure;
using Portfolio.Infrastructure.Authentication;

namespace Portfolio.UnitTests.Authentication;

public sealed class JwtConfigurationTests
{
    [Fact]
    public void Empty_signing_key_fails_options_validation()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = string.Empty,
            ["Jwt:Issuer"] = "Portfolio",
            ["Jwt:Audience"] = "Portfolio.Admin",
            ["Jwt:SecretKey"] = string.Empty,
            ["Jwt:AccessTokenMinutes"] = "15",
            ["Jwt:RefreshTokenDays"] = "7"
        }).Build();
        var services = new ServiceCollection().AddInfrastructure(configuration).BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
            services.GetRequiredService<IOptions<JwtSettings>>().Value);
    }
}
