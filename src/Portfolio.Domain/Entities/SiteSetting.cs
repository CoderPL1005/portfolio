using System.Text.Json;

namespace Portfolio.Domain.Entities;

public sealed class SiteSetting
{
    public string Key { get; set; } = null!;
    public JsonDocument Value { get; set; } = null!;
    public string? Description { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
