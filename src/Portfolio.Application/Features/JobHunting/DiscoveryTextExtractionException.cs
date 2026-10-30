namespace Portfolio.Application.Features.JobHunting;

public enum DiscoveryExtractionStage { REQUEST_SEND, HTTP_RESPONSE, RESPONSE_PARSE, RESPONSE_VALIDATION }
public enum DiscoveryExtractionCategory
{
    BAD_REQUEST, INVALID_ARGUMENT, FAILED_PRECONDITION, UNAUTHENTICATED, PERMISSION_DENIED, NOT_FOUND,
    RESOURCE_EXHAUSTED, INTERNAL, UNAVAILABLE, HTTP_REJECTED, TRANSPORT_UNKNOWN, TIMEOUT,
    MALFORMED_RESPONSE, OUTPUT_TOO_LARGE, OUTPUT_INCOMPLETE
}

// No provider exception, arbitrary message, request URI or response body is kept.
public sealed class DiscoveryTextExtractionException:Exception
{
    public int? HttpStatus { get; }
    public DiscoveryExtractionStage Stage { get; }
    public DiscoveryExtractionCategory Category { get; }
    public DiscoveryTextExtractionException(int? httpStatus,DiscoveryExtractionStage stage,DiscoveryExtractionCategory category)
        :base("DISCOVERY_SEMANTIC_UNAVAILABLE")
    {
        if(httpStatus is <100 or >599||!Enum.IsDefined(stage)||!Enum.IsDefined(category))throw new ArgumentOutOfRangeException();
        HttpStatus=httpStatus;Stage=stage;Category=category;
        if(httpStatus is not null)Data["HttpStatus"]=httpStatus.Value;
        Data["Stage"]=stage.ToString();Data["Category"]=category.ToString();
    }
}
