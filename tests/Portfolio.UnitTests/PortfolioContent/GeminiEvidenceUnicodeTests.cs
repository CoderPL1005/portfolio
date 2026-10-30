using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using GeminiJdSmoke;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class GeminiEvidenceUnicodeTests
{
    private static readonly Type Bridge = typeof(SmokeRunner).Assembly.GetType("GeminiJdSmoke.ValidationBridge")!;
    private const BindingFlags StaticInternal = BindingFlags.Static | BindingFlags.NonPublic;

    [Fact]
    public void All_redirected_pipes_explicitly_use_strict_UTF8_without_host_code_page_dependency()
    {
        var method = Bridge.GetMethod("CreateProcessInfo", StaticInternal);
        Assert.NotNull(method);
        var info = Assert.IsType<ProcessStartInfo>(method.Invoke(null, new object[] { "synthetic-script.mjs", "--preflight" }));
        foreach (var encoding in new[] { info.StandardInputEncoding, info.StandardOutputEncoding, info.StandardErrorEncoding })
        {
            Assert.NotNull(encoding);
            Assert.Equal(65001, encoding.CodePage);
            Assert.IsType<DecoderExceptionFallback>(encoding.DecoderFallback);
        }
        Assert.DoesNotContain("Gemini__ApiKey", info.Environment.Keys);
        Assert.DoesNotContain("NODE_OPTIONS", info.Environment.Keys);
    }

    [Theory]
    [InlineData("Hà Nội")]
    [InlineData("tuyển dụng")]
    [InlineData("thực tập sinh")]
    [InlineData("😀")]
    [InlineData("😀 Hà Nội")]
    [InlineData("A𠮷B")]
    public async Task CSharp_to_Node_and_back_preserves_Vietnamese_and_UTF16_surrogate_spans(string value)
    {
        var source = "😀 Kiểm tra: " + value + ".";
        var start = source.IndexOf(value, 2, StringComparison.Ordinal);
        var output = GeminiJdSmokeTests.Payload();
        output["employer"] = new { value, status = "POST_MENTIONED", evidence = new[] { new { start, end = start + value.Length, quote = value } } };
        output["sharedLocation"] = new { value = (string?)null, status = "UNKNOWN", evidence = Array.Empty<object>() };
        var relaxed = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        // Real UTF-8 over the existing process boundary, not only ASCII \u escapes.
        var input = JsonSerializer.Serialize(new { rawContent = source, output = JsonSerializer.Serialize(output, relaxed) }, relaxed);
        var path = (string)Bridge.GetMethod("Find", StaticInternal)!.Invoke(null, null)!;
        var task = (Task<JsonElement>)Bridge.GetMethod("Invoke", StaticInternal)!.Invoke(null, new object[] { path, "--validate", input })!;
        var report = await task;
        Assert.Equal("EVIDENCE_PASS", report.GetProperty("Evidence").GetString());
        Assert.Equal(value, report.GetProperty("Fields").GetProperty("employer").GetString());
        Assert.DoesNotContain("rawContent", report.GetRawText());
    }
}
