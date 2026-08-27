using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Persistence.Seeding;

public sealed class AdminSeeder(
    IApplicationDbContext dbContext,
    IConfiguration configuration,
    IPasswordHasher passwordHasher)
{
    public async Task<bool> SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!AdminBootstrapCredentials.TryCreate(configuration, out var credentials))
        {
            return false;
        }

        var existingEmails = await dbContext.AdminUsers.AsNoTracking()
            .Select(item => item.Email)
            .ToListAsync(cancellationToken);
        if (existingEmails.Any(email => AdminBootstrapCredentials.IsSameEmail(email, credentials.Email)))
        {
            return false;
        }

        var admin = new AdminUser
        {
            Id = Guid.NewGuid(), Email = credentials.Email, FullName = credentials.FullName,
            IsActive = true
        };
        admin.PasswordHash = passwordHasher.Hash(credentials.Password);
        dbContext.AdminUsers.Add(admin);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public sealed record AdminBootstrapCredentials(string Email, string Password, string? FullName)
{
    public static bool IsSameEmail(string first, string second) =>
        string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

    public static bool TryCreate(IConfiguration configuration, out AdminBootstrapCredentials credentials)
    {
        var email = configuration["AdminBootstrap:Email"];
        var password = configuration["AdminBootstrap:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            credentials = null!;
            return false;
        }

        credentials = new AdminBootstrapCredentials(
            email.Trim(), password, configuration["AdminBootstrap:FullName"]);
        return true;
    }
}
