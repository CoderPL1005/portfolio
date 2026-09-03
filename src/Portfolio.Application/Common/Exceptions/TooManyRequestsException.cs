namespace Portfolio.Application.Common.Exceptions;

public sealed class TooManyRequestsException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
