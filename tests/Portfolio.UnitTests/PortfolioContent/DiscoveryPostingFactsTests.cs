using Portfolio.Application.Features.JobHunting;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class DiscoveryPostingFactsTests
{
    [Theory]
    [InlineData("Salary: 10,000,000–15,000,000 VND/month")]
    [InlineData("Lương: 10-15 triệu VND/tháng")]
    public void Explicit_amounts_units_and_period_are_evidenced(string raw)
    {
        var facts=DiscoveryPostingFacts.Extract(raw);
        Assert.Equal(10000000m,facts["salaryMinimum"].Value);Assert.Equal(15000000m,facts["salaryMaximum"].Value);
        Assert.Equal("VND",facts["salaryCurrency"].Value);Assert.Equal("MONTHLY",facts["salaryPeriod"].Value);
        Assert.All(facts.Values,x=>Assert.Equal(raw,x.Quote));
    }
    [Theory]
    [InlineData("Salary: 10-15")][InlineData("Salary: 12.34 VND")][InlineData("Salary: 10,00,000 VND")]
    [InlineData("Salary: 20-10 USD")][InlineData("Salary: 10 triệu USD")][InlineData("Salary: 0 USD")]
    [InlineData("Salary: 10 USD\nSalary: 20 USD")]
    [InlineData("Published: 2026-11-30T17:00:00Z")][InlineData("Deadline: 2026-11-30")]
    [InlineData("Deadline: 2026-02-30T17:00:00Z")]
    public void Ambiguous_or_unsupported_values_abstain(string raw)=>Assert.Empty(DiscoveryPostingFacts.Extract(raw));
    [Fact]public void Deadline_keeps_explicit_offset_not_collection_time()
    {Assert.Equal("2026-11-30T17:00:00+07:00",DiscoveryPostingFacts.Extract("Deadline: 2026-11-30T17:00:00+07:00")["expiresAt"].Value);}
    [Fact]public void Explicit_Vietnamese_and_hyphenated_employment_and_workplace_types_are_evidenced()
    {
        var facts=DiscoveryPostingFacts.Extract("Hình thức làm việc: Toàn thời gian\nNơi làm việc: Hybrid\nHướng dẫn ứng tuyển: Gửi CV qua email hr@example.com");
        Assert.Equal("FULL_TIME",facts["employmentType"].Value);
        Assert.Equal("Hình thức làm việc: Toàn thời gian",facts["employmentType"].Quote);
        Assert.Equal("HYBRID",facts["workplaceType"].Value);
        Assert.Equal("Nơi làm việc: Hybrid",facts["workplaceType"].Quote);
        Assert.Equal("Gửi CV qua email hr@example.com",facts["notes"].Value);
        var en=DiscoveryPostingFacts.Extract("Employment type: Full-time\nWorkplace type: On-site");
        Assert.Equal("FULL_TIME",en["employmentType"].Value);
        Assert.Equal("ONSITE",en["workplaceType"].Value);
    }
}
