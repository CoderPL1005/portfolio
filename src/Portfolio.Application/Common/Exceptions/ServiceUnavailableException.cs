namespace Portfolio.Application.Common.Exceptions;
public sealed class ServiceUnavailableException(string code,string message):Exception(message){public string Code{get;}=code;}
