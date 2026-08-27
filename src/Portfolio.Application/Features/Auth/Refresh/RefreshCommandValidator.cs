using Portfolio.Application.Common.Abstractions.Validation;

namespace Portfolio.Application.Features.Auth.Refresh;

public sealed class RefreshCommandValidator : IRequestValidator<RefreshCommand>
{
    public const int MaximumRefreshTokenLength = 512;

    public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(
        RefreshCommand request,
        CancellationToken cancellationToken = default)
    {
        var failures = ValidateToken(request.RefreshToken);
        return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(failures);
    }

    internal static List<ValidationFailure> ValidateToken(string token)
    {
        var failures = new List<ValidationFailure>();
        if (string.IsNullOrWhiteSpace(token))
        {
            failures.Add(new("refreshToken", "Refresh token is required."));
        }
        else if (token.Length > MaximumRefreshTokenLength)
        {
            failures.Add(new("refreshToken", $"Refresh token must not exceed {MaximumRefreshTokenLength} characters."));
        }

        return failures;
    }
}
