using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace GeminiJdSmoke;

internal sealed record LocalValidation(string JsonParse, string Schema, string Evidence, string Coverage, string? ErrorCategory,
    JsonElement? Fields, JsonElement? Diagnostics, JsonElement? MissingFacts);
internal static class ValidationBridge
{
    internal static string Find()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var path = new DirectoryInfo(start); path is not null; path = path.Parent)
            {
                var candidate = Path.Combine(path.FullName, "tools", "gemini-jd-smoke", "validate-output.mjs");
                if (File.Exists(candidate)) return candidate;
            }
        throw new InvalidOperationException();
    }
    internal static async Task Preflight(string script)
    {
        var result = await Invoke(script, "--preflight", null);
        if (result.GetProperty("ready").ValueKind != JsonValueKind.True) throw new InvalidOperationException();
    }
    internal static async Task<LocalValidation> Validate(string script, string output)
    {
        var result = await Invoke(script, "--validate", JsonSerializer.Serialize(new { rawContent = SmokeRunner.Source, output }));
        return JsonSerializer.Deserialize<LocalValidation>(result.GetRawText())!;
    }
    internal static ProcessStartInfo CreateProcessInfo(string script, string mode)
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        var info = new ProcessStartInfo("node") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            StandardInputEncoding = utf8, StandardOutputEncoding = utf8, StandardErrorEncoding = utf8 };
        info.ArgumentList.Add(script); info.ArgumentList.Add(mode);
        // Do not propagate API keys, DB configuration, NODE_OPTIONS or preload hooks.
        info.Environment.Clear();
        foreach (var name in new[] { "PATH", "SystemRoot", "TEMP", "TMP" })
            if (Environment.GetEnvironmentVariable(name) is { } value) info.Environment[name] = value;
        return info;
    }
    private static async Task<JsonElement> Invoke(string script, string mode, string? input)
    {
        var info = CreateProcessInfo(script, mode);
        using var process = Process.Start(info) ?? throw new InvalidOperationException();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            var stdout = ReadBounded(process.StandardOutput, deadline.Token);
            var stderr = ReadBounded(process.StandardError, deadline.Token);
            if (input is not null) await process.StandardInput.WriteAsync(input.AsMemory(), deadline.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token);
            var text = await stdout; await stderr;
            if (process.ExitCode != 0) throw new InvalidOperationException();
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
    }
    private static async Task<string> ReadBounded(StreamReader stream, CancellationToken ct)
    {
        var text = new StringBuilder(); var buffer = new char[1024]; int n;
        while ((n = await stream.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            if (text.Length + n > 16384) throw new InvalidOperationException();
            text.Append(buffer, 0, n);
        }
        return text.ToString();
    }
}
