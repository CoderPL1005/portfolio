namespace Portfolio.Application.Common.Abstractions.Validation;

public sealed record ValidationFailure(string PropertyName, string Message);
