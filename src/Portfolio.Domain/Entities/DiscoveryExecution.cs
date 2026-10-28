namespace Portfolio.Domain.Entities;

public sealed class DiscoveryExecution
{
    public Guid Id { get; set; }
    public Guid ClientRequestId { get; set; }
    public Guid? CreatedByAdminId { get; set; }
    public string TriggerSource { get; set; } = "ADMIN_MANUAL";
    public string State { get; set; } = "CREATED";
    // NULL only after a known, fully persisted terminal outcome. UNKNOWN holds it.
    public string? ActiveKey { get; set; } = "DISCOVERY";
    public string ConfigurationJson { get; set; } = "{}";
    public string? WorkflowRunId { get; set; }
    public string? ResultHash { get; set; }
    public string? ErrorCode { get; set; }
    public int SemanticAttempts { get; set; }
    public int Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class DiscoveryExecutionAttempt
{
    public Guid Id { get; set; }
    public Guid ExecutionId { get; set; }
    public string WorkflowRunId { get; set; } = null!;
    public string Outcome { get; set; } = "CLAIMED";
    public int? ActualRequests { get; set; }
    public decimal? CreditsDeducted { get; set; }
    public decimal? CreditsUsedAlternateHeader { get; set; }
    public int? HttpStatus { get; set; }
    public DateTimeOffset ClaimedAt { get; set; }
}

public sealed class DiscoveryExecutionObservation
{
    public Guid Id { get; set; }
    public Guid ExecutionId { get; set; }
    public int ItemIndex { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string PayloadHash { get; set; } = null!;
    public string? ContentHash { get; set; }
    public Guid? RawJobPostingId { get; set; }
    public string SemanticState { get; set; } = "NOT_REQUESTED";
    public string? SemanticCacheKey { get; set; }
    public string? SemanticOutputJson { get; set; }
}

public sealed class DiscoveryExecutionVacancy
{
    public Guid Id { get; set; }
    public Guid ObservationId { get; set; }
    public int VacancyIndex { get; set; }
    public Guid? RawJobPostingId { get; set; }
    public Guid? JobPostingId { get; set; }
    public string Fingerprint { get; set; } = null!;
    public string Status { get; set; } = "NEEDS_REVIEW";
    public string FieldsJson { get; set; } = "{}";
    public string MissingFieldsJson { get; set; } = "[]";
}

public sealed class DiscoveryExecutionEvent
{
    public Guid Id { get; set; }
    public Guid ExecutionId { get; set; }
    public string State { get; set; } = null!;
    public string? Reason { get; set; }
    public DateTimeOffset At { get; set; }
}
