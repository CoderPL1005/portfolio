using System.Net.Mail;
using Portfolio.Application.Common.Abstractions.Validation;

namespace Portfolio.Application.Features.Auth.Login;

public sealed class LoginCommandValidator : IRequestValidator<LoginCommand>
{
    public const int MaximumPasswordLength = 256;

    public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(
        LoginCommand request,
        CancellationToken cancellationToken = default)
    {
        var failures = new List<ValidationFailure>();
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            failures.Add(new("email", "Email is required."));
        }
        else
        {
            if (request.Email.Length > 255)
            {
                failures.Add(new("email", "Email must not exceed 255 characters."));
            }
            else if (!MailAddress.TryCreate(request.Email, out _))
            {
                failures.Add(new("email", "Email must be valid."));
            }
        }

        if (string.IsNullOrEmpty(request.Password))
        {
            failures.Add(new("password", "Password is required."));
        }
        else if (request.Password.Length > MaximumPasswordLength)
        {
            failures.Add(new("password", $"Password must not exceed {MaximumPasswordLength} characters."));
        }

        return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(failures);
    }
}
