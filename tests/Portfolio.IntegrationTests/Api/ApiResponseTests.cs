using System.Text.Json;
using Portfolio.Api.Contracts.Common;

namespace Portfolio.IntegrationTests.Api;

public sealed class ApiResponseTests
{
    [Fact]
    public void Success_response_matches_contract_shape()
    {
        var json = JsonSerializer.Serialize(
            ApiResponse<object>.Ok(new { value = 42 }),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);

        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(42, document.RootElement.GetProperty("data").GetProperty("value").GetInt32());
        Assert.False(document.RootElement.TryGetProperty("error", out _));
    }
}
