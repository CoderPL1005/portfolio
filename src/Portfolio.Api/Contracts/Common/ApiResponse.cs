using System.Text.Json.Serialization;

namespace Portfolio.Api.Contracts.Common;

public sealed record ApiResponse
{
    private ApiResponse(bool success, ApiError? error)
    {
        Success = success;
        Error = error;
    }

    public bool Success { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ApiError? Error { get; }

    public static ApiResponse Ok() => new(true, null);

    public static ApiResponse Failure(ApiError error) => new(false, error);
}

public sealed record ApiResponse<T>
{
    private ApiResponse(bool success, T? data, ApiError? error)
    {
        Success = success;
        Data = data;
        Error = error;
    }

    public bool Success { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public T? Data { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ApiError? Error { get; }

    public static ApiResponse<T> Ok(T data) => new(true, data, null);

    public static ApiResponse<T> Failure(ApiError error) => new(false, default, error);
}
