using Microsoft.AspNetCore.Identity;
using Portfolio.Application.Common.Abstractions.Authentication;

namespace Portfolio.Infrastructure.Authentication;

public sealed class PasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<object> _hasher = new();
    private static readonly object User = new();

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        return _hasher.HashPassword(User, password);
    }

    public bool Verify(string passwordHash, string providedPassword)
    {
        if (string.IsNullOrEmpty(passwordHash) || string.IsNullOrEmpty(providedPassword))
        {
            return false;
        }

        return _hasher.VerifyHashedPassword(User, passwordHash, providedPassword) !=
            PasswordVerificationResult.Failed;
    }
}
