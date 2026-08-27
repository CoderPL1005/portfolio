using Portfolio.Application.Common.Abstractions.Validation;

namespace Portfolio.Application.Common.Exceptions;

public sealed class ValidationException : Exception
{
    public const string ErrorCode = "VALIDATION_ERROR";
    public const string ErrorMessage = "One or more validation errors occurred.";

    public ValidationException(IEnumerable<ValidationFailure> failures)
        : base(ErrorMessage)
    {
        Errors = failures
            .GroupBy(failure => failure.PropertyName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.Message).Distinct().ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
