namespace Portfolio.Application.Common.Abstractions.Validation;

public interface IRequestValidator<in TRequest>
{
    Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(
        TRequest request,
        CancellationToken cancellationToken = default);
}
