using Portfolio.Application.Features.Auth.Login;
using Portfolio.Application.Features.Auth.Logout;
using Portfolio.Application.Features.Auth.Refresh;

namespace Portfolio.UnitTests.Authentication;

public sealed class AuthValidationTests
{
    [Fact]
    public async Task Login_rejects_invalid_email_and_oversized_password()
    {
        var validator = new LoginCommandValidator();
        var failures = await validator.ValidateAsync(new LoginCommand(
            "not-an-email",
            new string('x', LoginCommandValidator.MaximumPasswordLength + 1)));

        Assert.Contains(failures, failure => failure.PropertyName == "email");
        Assert.Contains(failures, failure => failure.PropertyName == "password");
    }

    [Fact]
    public async Task Refresh_and_logout_reject_oversized_tokens()
    {
        var token = new string('x', RefreshCommandValidator.MaximumRefreshTokenLength + 1);
        var refreshFailures = await new RefreshCommandValidator().ValidateAsync(new RefreshCommand(token));
        var logoutFailures = await new LogoutCommandValidator().ValidateAsync(new LogoutCommand(token));

        Assert.Contains(refreshFailures, failure => failure.PropertyName == "refreshToken");
        Assert.Contains(logoutFailures, failure => failure.PropertyName == "refreshToken");
    }
}
