using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.Auth.GetCurrentAdmin;
using Portfolio.Application.Features.Auth.Login;
using Portfolio.Application.Features.Auth.Logout;
using Portfolio.Application.Features.Auth.Refresh;
using Portfolio.Domain.Entities;
using Portfolio.Infrastructure.Authentication;
using Portfolio.Infrastructure.Persistence.Seeding;

namespace Portfolio.UnitTests.Authentication;

public sealed class AuthHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Valid_login_returns_tokens_and_persists_only_refresh_hash()
    {
        await using var context = CreateContext();
        var hasher = new PasswordHasher();
        var admin = CreateAdmin(hasher, isActive: true);
        context.AdminUsers.Add(admin);
        await context.SaveChangesAsync();
        var jwt = new FakeJwtTokenService(Now);
        var handler = new LoginCommandHandler(context, hasher, jwt, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new LoginCommand(" ADMIN@example.com ", "correct-password"));
        var stored = await context.AdminRefreshTokens.SingleAsync();

        Assert.NotEmpty(result.AccessToken);
        Assert.StartsWith("raw-refresh-", result.RefreshToken, StringComparison.Ordinal);
        Assert.Equal(jwt.HashRefreshToken(result.RefreshToken), stored.TokenHash);
        Assert.NotEqual(result.RefreshToken, stored.TokenHash);
        Assert.Equal(Now, admin.LastLoginAt);
    }

    [Fact]
    public async Task Unknown_email_and_wrong_password_have_identical_public_failure()
    {
        await using var context = CreateContext();
        var hasher = new PasswordHasher();
        context.AdminUsers.Add(CreateAdmin(hasher, isActive: true));
        await context.SaveChangesAsync();
        var handler = new LoginCommandHandler(
            context, hasher, new FakeJwtTokenService(Now), new FixedTimeProvider(Now));

        var unknown = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            handler.HandleAsync(new LoginCommand("unknown@example.com", "wrong-password")));
        var wrong = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            handler.HandleAsync(new LoginCommand("admin@example.com", "wrong-password")));

        Assert.Equal(unknown.Code, wrong.Code);
        Assert.Equal(unknown.Message, wrong.Message);
        Assert.Equal("INVALID_CREDENTIALS", wrong.Code);
    }

    [Fact]
    public async Task Disabled_admin_cannot_log_in()
    {
        await using var context = CreateContext();
        var hasher = new PasswordHasher();
        context.AdminUsers.Add(CreateAdmin(hasher, isActive: false));
        await context.SaveChangesAsync();
        var handler = new LoginCommandHandler(
            context, hasher, new FakeJwtTokenService(Now), new FixedTimeProvider(Now));

        var exception = await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.HandleAsync(new LoginCommand("admin@example.com", "correct-password")));

        Assert.Equal("ADMIN_DISABLED", exception.Code);
    }

    [Theory]
    [InlineData(RefreshRotationStatus.Invalid, "INVALID_REFRESH_TOKEN")]
    [InlineData(RefreshRotationStatus.Expired, "REFRESH_TOKEN_EXPIRED")]
    [InlineData(RefreshRotationStatus.Revoked, "REFRESH_TOKEN_REVOKED")]
    public async Task Invalid_refresh_states_do_not_issue_access_tokens(
        RefreshRotationStatus status,
        string expectedCode)
    {
        var store = new FakeRefreshTokenStore(() => new RefreshRotationResult(status));
        var handler = new RefreshCommandHandler(
            store, new FakeJwtTokenService(Now), new FixedTimeProvider(Now));

        var exception = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            handler.HandleAsync(new RefreshCommand("old-token")));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public async Task Successful_refresh_rotates_and_hashes_both_tokens()
    {
        var adminId = Guid.NewGuid();
        var store = new FakeRefreshTokenStore(() =>
            new RefreshRotationResult(RefreshRotationStatus.Succeeded, adminId, "admin@example.com"));
        var jwt = new FakeJwtTokenService(Now);
        var handler = new RefreshCommandHandler(store, jwt, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new RefreshCommand("old-token"));

        Assert.Equal("HASH:old-token", store.LastCurrentHash);
        Assert.Equal(jwt.HashRefreshToken(result.RefreshToken), store.LastReplacementHash);
        Assert.NotEqual(result.RefreshToken, store.LastReplacementHash);
        Assert.NotEmpty(result.AccessToken);
    }

    [Fact]
    public async Task Two_concurrent_rotation_attempts_have_at_most_one_success()
    {
        var claimed = 0;
        var adminId = Guid.NewGuid();
        var store = new FakeRefreshTokenStore(() =>
            Interlocked.CompareExchange(ref claimed, 1, 0) == 0
                ? new RefreshRotationResult(RefreshRotationStatus.Succeeded, adminId, "admin@example.com")
                : new RefreshRotationResult(RefreshRotationStatus.Revoked));
        var handler = new RefreshCommandHandler(
            store, new FakeJwtTokenService(Now), new FixedTimeProvider(Now));

        var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try
            {
                await handler.HandleAsync(new RefreshCommand("same-old-token"));
                return true;
            }
            catch (UnauthorizedException)
            {
                return false;
            }
        }));

        Assert.Equal(1, attempts.Count(success => success));
    }

    [Fact]
    public async Task Logout_revocation_is_idempotent_at_handler_boundary()
    {
        var store = new FakeRefreshTokenStore();
        var handler = new LogoutCommandHandler(
            store,
            new FakeJwtTokenService(Now),
            new FakeCurrentUser(Guid.NewGuid()),
            new FixedTimeProvider(Now));

        await handler.HandleAsync(new LogoutCommand("same-token"));
        await handler.HandleAsync(new LogoutCommand("same-token"));

        Assert.Equal(2, store.RevocationCalls);
        Assert.Equal("HASH:same-token", store.LastCurrentHash);
    }

    [Fact]
    public async Task Current_admin_result_excludes_password_and_token_fields()
    {
        await using var context = CreateContext();
        var admin = CreateAdmin(new PasswordHasher(), isActive: true);
        admin.LastLoginAt = Now;
        context.AdminUsers.Add(admin);
        await context.SaveChangesAsync();
        var handler = new GetCurrentAdminQueryHandler(context, new FakeCurrentUser(admin.Id, admin.Email));

        var result = await handler.HandleAsync(new GetCurrentAdminQuery());

        Assert.Equal(admin.Id, result.Id);
        Assert.Equal(Now, result.LastLoginAt);
        Assert.DoesNotContain(result.GetType().GetProperties(), property =>
            property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Token", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Admin_seeder_uses_shared_password_hasher()
    {
        await using var context = CreateContext();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AdminBootstrap:Email"] = "admin@example.com",
            ["AdminBootstrap:Password"] = "configured-password"
        }).Build();
        var hasher = new RecordingPasswordHasher();
        var seeder = new AdminSeeder(context, configuration, hasher);

        Assert.True(await seeder.SeedAsync());
        var admin = await context.AdminUsers.SingleAsync();
        Assert.Equal("HASHED:configured-password", admin.PasswordHash);
        Assert.Equal(1, hasher.HashCalls);
    }

    private static AuthTestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AuthTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AuthTestDbContext(options);
    }

    private static AdminUser CreateAdmin(PasswordHasher hasher, bool isActive) => new()
    {
        Id = Guid.NewGuid(),
        Email = "admin@example.com",
        PasswordHash = hasher.Hash("correct-password"),
        FullName = "Portfolio Admin",
        IsActive = isActive
    };

    private sealed class RecordingPasswordHasher : IPasswordHasher
    {
        public int HashCalls { get; private set; }

        public string Hash(string password)
        {
            HashCalls++;
            return $"HASHED:{password}";
        }

        public bool Verify(string passwordHash, string providedPassword) => false;
    }
}
