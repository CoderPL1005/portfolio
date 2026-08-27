using System.Text.Json.Serialization;

namespace Portfolio.Api.Contracts.Common;

public sealed record ApiError(
    string Code,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, string[]>? Details = null);
