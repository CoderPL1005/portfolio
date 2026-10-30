using System.Text.Json;
using GeminiJdSmoke;

if (args.Any(arg => arg != "--live") || args.Count(arg => arg == "--live") > 1)
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/gemini-jd-smoke [-- --live]");
    return 2;
}
var live = args.Contains("--live");
var report = await SmokeRunner.RunAsync(
    new(live, live ? Environment.GetEnvironmentVariable("Gemini__ApiKey") : null,
        Environment.GetEnvironmentVariable("Gemini__JobExtractionModel")),
    Console.In, Console.Error, !Console.IsInputRedirected);
Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
return report.EndToEnd switch { "END_TO_END_PASS" => 0, "PARTIAL_EXTRACTION" or "END_TO_END_INCOMPLETE" => 2, _ => 1 };
