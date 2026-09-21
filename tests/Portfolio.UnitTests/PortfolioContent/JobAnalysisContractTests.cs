using Portfolio.Application.Common.Abstractions.AI;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class JobAnalysisContractTests
{
    [Fact]
    public void Contract_has_only_extraction_assessments_and_stable_versions()
    {
        Assert.Equal("1", JobAnalysisContract.SchemaVersion);
        Assert.Equal("1", JobAnalysisContract.PromptVersion);
        Assert.Equal(
            ["SINGLE_JOB_POSTING", "NOT_A_JOB_POSTING", "MULTIPLE_JOB_POSTINGS", "UNREADABLE_OR_INSUFFICIENT"],
            typeof(JobAnalysisAssessments).GetFields()
                .OrderBy(field => field.MetadataToken)
                .Select(field => (string)field.GetRawConstantValue()!));
    }
}
