namespace Portfolio.Application.Common.Exceptions;

public sealed class UnauthorizedException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
