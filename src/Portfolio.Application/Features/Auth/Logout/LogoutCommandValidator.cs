using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Features.Auth.Refresh;

namespace Portfolio.Application.Features.Auth.Logout;

public sealed class LogoutCommandValidator : IRequestValidator<LogoutCommand>
{
    public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(
        LogoutCommand request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<ValidationFailure>>(
            RefreshCommandValidator.ValidateToken(request.RefreshToken));
}
