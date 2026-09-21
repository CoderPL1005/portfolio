using Microsoft.Extensions.Options;
using Portfolio.Application.Common.Configuration;

namespace Portfolio.Application.Features.JobHunting;

public static class JobRecommendationValues
{
    public const string Recommended = "RECOMMENDED";
    public const string NeedsReview = "NEEDS_REVIEW";
    public const string NotRecommended = "NOT_RECOMMENDED";
}

public sealed class JobRecommendationPolicy(IOptions<JobRecommendationOptions> configured)
{
    private readonly JobRecommendationOptions options = configured.Value;

    public JobFitAnalysisResult Apply(JobFitAnalysisResult fit)
    {
        var recommendation = Recommend(fit.OverallScore, fit.CoveragePercent);
        var reasons = fit.Components.Where(component => component.Status == "MATCH")
            .Select(component => component.Explanation).Distinct(StringComparer.Ordinal).ToList();
        var concerns = fit.Components.Where(component => component.Status is "PARTIAL" or "MISMATCH" or "UNKNOWN")
            .Select(component => component.Explanation).Distinct(StringComparer.Ordinal).ToList();

        if (recommendation == JobRecommendationValues.Recommended)
            reasons.Insert(0, "Fit score and evidence coverage meet the configured recommendation criteria.");
        else if (recommendation == JobRecommendationValues.NotRecommended)
            concerns.Insert(0, "Fit score is at or below the configured not-recommended threshold with sufficient evidence coverage.");
        else if (!fit.OverallScore.HasValue)
            concerns.Insert(0, "No fit score can be calculated from the available evidence.");
        else if (fit.CoveragePercent < options.RecommendedMinimumCoverage)
            concerns.Insert(0, "Evidence coverage is below the configured recommendation threshold.");
        else
            concerns.Insert(0, "Fit score does not meet the configured recommendation threshold.");

        return fit with { Recommendation = recommendation, Reasons = reasons, Concerns = concerns };
    }

    public string Recommend(int? fitScore, int evidenceCoverage)
    {
        if (!fitScore.HasValue) return JobRecommendationValues.NeedsReview;
        if (evidenceCoverage >= options.RecommendedMinimumCoverage && fitScore >= options.RecommendedMinimumScore)
            return JobRecommendationValues.Recommended;
        if (evidenceCoverage >= options.NotRecommendedMinimumCoverage && fitScore <= options.NotRecommendedMaximumScore)
            return JobRecommendationValues.NotRecommended;
        return JobRecommendationValues.NeedsReview;
    }
}

public static class JobSelectionTransitionPolicy
{
    public static bool CanTransition(string current, string target) =>
        current is "PENDING_ANALYSIS" or "RECOMMENDED" && target is "APPROVED" or "SKIPPED";
}
