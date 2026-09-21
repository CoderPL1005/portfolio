namespace Portfolio.Application.Common.Exceptions;

public sealed class JobAnalysisRequestTooLargeException : Exception
{
    public JobAnalysisRequestTooLargeException()
        : base("The screenshots are too large to analyze in one inline request.")
    {
    }
}
