using Microsoft.Extensions.Options;

namespace Portfolio.Application.Common.Configuration;

public sealed class JobRecommendationOptions
{
    public const string SectionName = "JobRecommendation";
    public int RecommendedMinimumScore { get; set; } = 75;
    public int RecommendedMinimumCoverage { get; set; } = 70;
    public int NotRecommendedMaximumScore { get; set; } = 49;
    public int NotRecommendedMinimumCoverage { get; set; } = 70;
}

public sealed class JobRecommendationOptionsValidator : IValidateOptions<JobRecommendationOptions>
{
    public ValidateOptionsResult Validate(string? name, JobRecommendationOptions options)
    {
        var failures = new List<string>();
        ValidatePercentage(failures, nameof(options.RecommendedMinimumScore), options.RecommendedMinimumScore);
        ValidatePercentage(failures, nameof(options.RecommendedMinimumCoverage), options.RecommendedMinimumCoverage);
        ValidatePercentage(failures, nameof(options.NotRecommendedMaximumScore), options.NotRecommendedMaximumScore);
        ValidatePercentage(failures, nameof(options.NotRecommendedMinimumCoverage), options.NotRecommendedMinimumCoverage);
        if (options.NotRecommendedMaximumScore >= options.RecommendedMinimumScore)
            failures.Add("JobRecommendation:NotRecommendedMaximumScore must be less than RecommendedMinimumScore.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidatePercentage(List<string> failures, string name, int value)
    {
        if (value is < 0 or > 100)
            failures.Add($"JobRecommendation:{name} must be between 0 and 100.");
    }
}
