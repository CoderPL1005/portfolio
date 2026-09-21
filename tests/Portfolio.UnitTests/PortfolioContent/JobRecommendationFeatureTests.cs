using Microsoft.Extensions.Options;
using Portfolio.Application.Common.Configuration;
using Portfolio.Application.Features.JobHunting;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class JobRecommendationFeatureTests
{
    [Theory]
    [InlineData(75, 70, "RECOMMENDED")]
    [InlineData(74, 70, "NEEDS_REVIEW")]
    [InlineData(90, 69, "NEEDS_REVIEW")]
    [InlineData(49, 70, "NOT_RECOMMENDED")]
    [InlineData(50, 70, "NEEDS_REVIEW")]
    [InlineData(null, 100, "NEEDS_REVIEW")]
    public void Policy_uses_exact_score_and_coverage_boundaries(int? score, int coverage, string expected)
    {
        Assert.Equal(expected, Policy().Recommend(score, coverage));
    }

    [Fact]
    public void Policy_reuses_fit_components_for_deterministic_reasons_and_concerns()
    {
        var fit = Fit(82, 80,
            new("LOCATION", "Location", 100, 10, "MATCH", "Location matches a configured preference.", ["Hanoi"]),
            new("ROLE_ALIGNMENT", "Role alignment", 67, 25, "PARTIAL", "The job title partially overlaps a target role.", ["Backend Developer"]),
            new("SALARY", "Salary", null, 5, "UNKNOWN", "The job does not provide a comparable maximum salary.", []));

        var result = Policy().Apply(fit);

        Assert.Equal("RECOMMENDED", result.Recommendation);
        Assert.Contains("Location matches a configured preference.", result.Reasons);
        Assert.Contains("The job title partially overlaps a target role.", result.Concerns);
        Assert.Contains("The job does not provide a comparable maximum salary.", result.Concerns);
        Assert.Equal("UNKNOWN", result.Components.Single(x => x.Key == "SALARY").Status);
        Assert.Equal(fit.OverallScore, result.OverallScore);
        Assert.Equal(fit.CoveragePercent, result.CoveragePercent);
    }

    [Fact]
    public void Configuration_validation_rejects_out_of_range_and_overlapping_thresholds()
    {
        var validator = new JobRecommendationOptionsValidator();
        Assert.True(validator.Validate(null, new() { RecommendedMinimumScore = 101 }).Failed);
        Assert.True(validator.Validate(null, new() { RecommendedMinimumCoverage = -1 }).Failed);
        Assert.True(validator.Validate(null, new() { NotRecommendedMaximumScore = 75, RecommendedMinimumScore = 75 }).Failed);
        Assert.True(validator.Validate(null, new()).Succeeded);
    }

    [Theory]
    [InlineData("PENDING_ANALYSIS", "APPROVED", true)]
    [InlineData("PENDING_ANALYSIS", "SKIPPED", true)]
    [InlineData("RECOMMENDED", "APPROVED", true)]
    [InlineData("RECOMMENDED", "SKIPPED", true)]
    [InlineData("PENDING_ANALYSIS", "RECOMMENDED", false)]
    [InlineData("APPROVED", "SKIPPED", false)]
    [InlineData("SKIPPED", "APPROVED", false)]
    [InlineData("APPROVED", "APPROVED", false)]
    public void Human_selection_transition_policy_is_explicit_and_terminal(string current, string target, bool expected) =>
        Assert.Equal(expected, JobSelectionTransitionPolicy.CanTransition(current, target));

    private static JobRecommendationPolicy Policy() => new(Options.Create(new JobRecommendationOptions()));
    private static JobFitAnalysisResult Fit(int? score, int coverage, params JobFitComponentResult[] components) =>
        new(Guid.NewGuid(), 1, 1, score, 80, 100, coverage, "PENDING", components, [], [], [],
            components.Where(x => x.Status == "UNKNOWN").Select(x => x.Label).ToArray());
}
