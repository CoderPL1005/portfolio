using System.Text.Json;
using Portfolio.Domain.Constants;

namespace Portfolio.Domain.Entities;

public sealed class JobApplicationEvent
{
    public Guid Id { get; set; }
    public Guid JobApplicationId { get; set; }
    public string EventType { get; set; } = JobApplicationEventTypes.Created;
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public string ActorType { get; set; } = JobApplicationEventActorTypes.System;
    public Guid? ActorAdminUserId { get; set; }
    public string? Note { get; set; }
    public JsonDocument Metadata { get; set; } = null!;
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public JobApplication JobApplication { get; set; } = null!;
    public AdminUser? ActorAdminUser { get; set; }
}
