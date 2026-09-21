using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Portfolio.Application.Common.Configuration;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.JobHunting;
using Portfolio.Domain.Entities;
using Portfolio.UnitTests.Authentication;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class JobFitFeatureTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Scoring_normalizes_aliases_deduplicates_and_excludes_unknown_weights()
    {
        var scorer = Scorer();
        var job = Job("Backend Engineer", "Hanoi", "remote", "full-time", [".NET", "Angular", "angular", "Docker"]);
        job.ExperienceRequirements = null;
        var result = scorer.Score(job, Preferences(targets: ["Backend Developer"], preferred: ["dotnet", "Docker"], locations: ["Hanoi"], workplace: ["work from home"], employment: ["full time"]),
            new(["dotnet", "Angular"], ["Docker"], []));

        Assert.Equal(["angular", "dotnet"], result.MatchedTechnologies.Order());
        Assert.Equal(["docker"], result.DevelopingTechnologies);
        Assert.Empty(result.MissingTechnologies);
        Assert.Equal(8, result.Components.Count);
        Assert.Equal("PARTIAL", Component(result, "TECHNICAL_CAPABILITY").Status);
        Assert.Equal(67, Component(result, "TECHNICAL_CAPABILITY").Score);
        Assert.Equal("UNKNOWN", Component(result, "EXPERIENCE_REQUIREMENT").Status);
        Assert.DoesNotContain("Experience", result.Components.Where(x => x.Score.HasValue).Select(x => x.Label));
        Assert.Equal(80, result.AvailableWeight);
        Assert.Equal(80, result.CoveragePercent);
    }

    [Theory]
    [InlineData("3 years")]
    [InlineData("3+ years")]
    [InlineData("at least 3 years")]
    [InlineData("minimum 3 years")]
    [InlineData("tối thiểu 3 năm")]
    [InlineData("ít nhất 3 năm")]
    public void Explicit_general_experience_requirements_are_parsed(string requirement) =>
        Assert.Equal(3, JobFitScorer.ParseYears(requirement));

    [Theory]
    [InlineData("3 years preferred")]
    [InlineData("3 years of Angular")]
    [InlineData("senior experience")]
    public void Ambiguous_or_technology_specific_experience_is_unknown(string requirement) =>
        Assert.Null(JobFitScorer.ParseYears(requirement));

    [Fact]
    public void Overlapping_experience_is_not_double_counted()
    {
        var days = JobFitScorer.MergeDays([
            new(new DateOnly(2022, 1, 1), new DateOnly(2024, 1, 1)),
            new(new DateOnly(2023, 1, 1), new DateOnly(2025, 1, 1))]);
        Assert.Equal(new DateOnly(2025, 1, 1).DayNumber - new DateOnly(2022, 1, 1).DayNumber + 1, days);
    }

    [Fact]
    public void Available_weight_formula_salary_and_statuses_are_deterministic()
    {
        var scorer = Scorer(); var job = Job("Unrelated", "Da Nang", "hybrid", "contract", ["Postgres", "Docker"]);
        job.SalaryMaximum = 999; job.SalaryCurrency = "USD"; job.SalaryPeriod = "MONTH";
        var prefs = Preferences(targets: ["Backend Developer"], preferred: ["PostgreSQL"], locations: ["Hanoi"], workplace: ["remote"], employment: ["full time"], salary: 1000);
        var facts = new CandidateFactualSnapshot(["PostgreSQL"], [], []);
        var first = scorer.Score(job, prefs, facts); var second = scorer.Score(job, prefs, facts);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.Equal(85, first.AvailableWeight);
        Assert.Equal(100, first.TotalConfiguredWeight);
        Assert.Equal(85, first.CoveragePercent);
        Assert.Equal("MISMATCH", Component(first, "SALARY").Status);
        Assert.Equal("PARTIAL", Component(first, "TECHNICAL_CAPABILITY").Status);
        Assert.InRange(first.OverallScore!.Value, 0, 100);
    }

    [Fact]
    public void All_components_use_the_exact_available_weight_denominator()
    {
        var job = Job("Backend Developer", "Hanoi", "remote", "full-time", ["C#", "Docker"]);
        job.ExperienceRequirements = "1 year";
        job.SalaryMaximum = 1000; job.SalaryCurrency = "USD"; job.SalaryPeriod = "MONTH";
        var result = Scorer().Score(job,
            Preferences(targets: ["Backend Developer"], preferred: ["C#", "Docker"], locations: ["Hanoi"], workplace: ["remote"], employment: ["full time"], salary: 1000),
            new(["C#"], [], [new(new DateOnly(2024, 1, 1), new DateOnly(2025, 1, 1))]));

        Assert.Equal(100, result.AvailableWeight);
        Assert.Equal(100, result.TotalConfiguredWeight);
        Assert.Equal(100, result.CoveragePercent);
        Assert.Equal(50, Component(result, "TECHNICAL_CAPABILITY").Score);
        Assert.Equal(100, Component(result, "ROLE_ALIGNMENT").Score);
        Assert.Equal(100, Component(result, "EXPERIENCE_REQUIREMENT").Score);
        Assert.Equal(100, Component(result, "LOCATION").Score);
        Assert.Equal(100, Component(result, "PREFERRED_TECHNOLOGY_ALIGNMENT").Score);
        Assert.Equal(100, Component(result, "WORKPLACE_TYPE").Score);
        Assert.Equal(100, Component(result, "EMPLOYMENT_TYPE").Score);
        Assert.Equal(100, Component(result, "SALARY").Score);
        Assert.Equal(85, result.OverallScore);
    }

    [Fact]
    public void Role_and_technical_only_use_55_as_the_exact_denominator()
    {
        var result = Scorer().Score(Job("Backend Developer", "unknown", null, null, ["C#", "Angular", "Docker", "Postgres"]),
            Preferences(targets: ["Backend Developer"]), new(["C#", "Angular", "Docker"], [], []));

        Assert.Equal(55, result.AvailableWeight);
        Assert.Equal(55, result.CoveragePercent);
        Assert.Equal(86, result.OverallScore);
    }

    [Theory]
    [InlineData(null, "UNKNOWN", null)]
    [InlineData(0d, "MISMATCH", 0)]
    [InlineData(double.Epsilon, "PARTIAL", 1)]
    [InlineData(.5d, "PARTIAL", 50)]
    [InlineData(.9999999999999999d, "PARTIAL", 99)]
    [InlineData(1d, "MATCH", 100)]
    public void Component_status_and_numeric_score_use_exact_bounded_boundaries(double? score, string expectedStatus, int? expectedPercentage)
    {
        Assert.Equal(expectedStatus, JobFitScorer.Status(score));
        Assert.Equal(expectedPercentage, JobFitScorer.Percentage(score));
    }

    [Fact]
    public void Salary_is_unknown_without_maximum_or_with_incompatible_units()
    {
        var scorer = Scorer(); var prefs = Preferences(salary: 1000);
        var onlyMinimum = Job("Role", "Hanoi", null, null, []); onlyMinimum.SalaryMinimum = 1500; onlyMinimum.SalaryCurrency = "USD"; onlyMinimum.SalaryPeriod = "MONTH";
        Assert.Equal("UNKNOWN", Component(scorer.Score(onlyMinimum, prefs, new([], [], [])), "SALARY").Status);
        onlyMinimum.SalaryMaximum = 2000; onlyMinimum.SalaryCurrency = "VND";
        Assert.Equal("UNKNOWN", Component(scorer.Score(onlyMinimum, prefs, new([], [], [])), "SALARY").Status);
    }

    [Fact]
    public void No_evaluable_components_produces_null_score_and_zero_coverage()
    {
        var result=Scorer().Score(Job("Role","unknown",null,null,[]),Preferences(),new([],[],[]));
        Assert.Null(result.OverallScore);Assert.Equal(0,result.AvailableWeight);Assert.Equal(0,result.CoveragePercent);Assert.All(result.Components,x=>Assert.Equal("UNKNOWN",x.Status));
    }

    [Theory]
    [InlineData("Backend Developer","Backend Developer","MATCH")]
    [InlineData("Backend Engineer","Backend Developer","MATCH")]
    [InlineData("Frontend Developer","Backend Developer","MISMATCH")]
    public void Role_matching_is_exact_alias_based_or_conservative(string jobRole,string target,string expected)
    {
        var result=Scorer().Score(Job(jobRole,"Hanoi",null,null,[]),Preferences(targets:[target]),new([],[],[]));
        Assert.Equal(expected,Component(result,"ROLE_ALIGNMENT").Status);
    }

    [Fact]
    public void Workplace_employment_and_location_use_explicit_normalization_without_inference()
    {
        var result=Scorer().Score(Job("Role","Ha Noi","on-site","full-time",[]),Preferences(locations:["Da Nang"],workplace:["onsite"],employment:["full time"]),new([],[],[]));
        Assert.Equal("MISMATCH",Component(result,"LOCATION").Status);Assert.Equal("MATCH",Component(result,"WORKPLACE_TYPE").Status);Assert.Equal("MATCH",Component(result,"EMPLOYMENT_TYPE").Status);
        var missing=Scorer().Score(Job("Role","multiple locations",null,null,[]),Preferences(locations:["Hanoi"],workplace:["remote"],employment:["internship"]),new([],[],[]));
        Assert.Equal("UNKNOWN",Component(missing,"LOCATION").Status);Assert.Equal("UNKNOWN",Component(missing,"WORKPLACE_TYPE").Status);Assert.Equal("UNKNOWN",Component(missing,"EMPLOYMENT_TYPE").Status);
    }

    [Fact]
    public void Experience_exactly_satisfying_and_below_minimum_are_match_and_partial()
    {
        var job=Job("Role","Hanoi",null,null,[]);job.ExperienceRequirements="3+ years";
        var exact=new CandidateFactualSnapshot([],[],[new(new DateOnly(2023,1,1),new DateOnly(2025,12,31))]);
        var match=Component(Scorer().Score(job,Preferences(),exact),"EXPERIENCE_REQUIREMENT");Assert.Equal("MATCH",match.Status);
        var below=new CandidateFactualSnapshot([],[],[new(new DateOnly(2025,1,1),new DateOnly(2025,12,31))]);
        Assert.Equal("PARTIAL",Component(Scorer().Score(job,Preferences(),below),"EXPERIENCE_REQUIREMENT").Status);
    }

    [Fact]
    public void Salary_maximum_at_floor_matches_and_below_floor_mismatches()
    {
        var job=Job("Role","Hanoi",null,null,[]);job.SalaryCurrency="USD";job.SalaryPeriod="MONTH";job.SalaryMaximum=1000;
        Assert.Equal("MATCH",Component(Scorer().Score(job,Preferences(salary:1000),new([],[],[])),"SALARY").Status);
        job.SalaryMaximum=999.99m;Assert.Equal("MISMATCH",Component(Scorer().Score(job,Preferences(salary:1000),new([],[],[])),"SALARY").Status);
    }

    [Fact]
    public async Task Preferences_create_normalize_update_and_reject_stale_version()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var handler = new UpdateCandidateJobPreferencesCommandHandler(db, new FixedTimeProvider(Now));
        var created = await handler.HandleAsync(Command(0, [" Backend Developer ", "backend developer"]));
        Assert.Equal(1, created.Version); Assert.Single(created.TargetRoles); Assert.Equal("USD", created.SalaryCurrency);
        var updated = await handler.HandleAsync(Command(1, ["Full Stack Developer"]));
        Assert.Equal(2, updated.Version);
        await Assert.ThrowsAsync<ConflictException>(() => handler.HandleAsync(Command(1, ["Stale"])));
        Assert.Single(await db.CandidateJobPreferences.ToListAsync());
    }

    [Fact]
    public async Task Preference_validation_enforces_salary_consistency_and_value_quality()
    {
        var validator = new UpdateCandidateJobPreferencesCommandValidator();
        var invalid = Command(-1, [""]) with { MinimumSalary = 1, SalaryCurrency = null, SalaryPeriod = null };
        Assert.NotEmpty(await validator.ValidateAsync(invalid));
        var valid = Command(0, ["Backend Developer"]);
        Assert.Empty(await validator.ValidateAsync(valid));
    }

    [Fact]
    public async Task Snapshot_uses_only_published_evidence_and_not_active_catalog_alone()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var used = Tech("Used"); var projectUsed=Tech("Project Used"); var catalogOnly = Tech("Catalog only"); var learning = Tech("Learning"); var exploring = Tech("Exploring");
        var inactiveExperience = Tech("Inactive Experience"); inactiveExperience.IsActive = false;
        var inactiveProject = Tech("Inactive Project"); inactiveProject.IsActive = false;
        var publishedExperience = Experience(true); var hiddenExperience = Experience(false);
        var project=new Project{Id=Guid.NewGuid(),Slug="public",Title="Public",Status="COMPLETED",IsPublished=true};
        var hiddenProject=new Project{Id=Guid.NewGuid(),Slug="hidden",Title="Hidden",Status="COMPLETED",IsPublished=false};
        db.AddRange(used, projectUsed, catalogOnly, learning, exploring, inactiveExperience, inactiveProject, publishedExperience, hiddenExperience,project,hiddenProject);
        db.Skills.AddRange(
            new Skill { Id = Guid.NewGuid(), Name = "Skill Used", Category = "Backend", ExperienceLevel = "USED", TechnologyId = used.Id, IsPublished = true },
            new Skill { Id = Guid.NewGuid(), Name = "Skill Learning", Category = "Backend", ExperienceLevel = "LEARNING", TechnologyId = learning.Id, IsPublished = true },
            new Skill { Id = Guid.NewGuid(), Name = "Skill Exploring", Category = "Backend", ExperienceLevel = "EXPLORING", TechnologyId = exploring.Id, IsPublished = true },
            new Skill { Id = Guid.NewGuid(), Name = "Hidden", Category = "Backend", ExperienceLevel = "USED", IsPublished = false });
        db.ExperienceTechnologies.AddRange(new ExperienceTechnology { ExperienceId = publishedExperience.Id, TechnologyId = used.Id }, new ExperienceTechnology { ExperienceId = publishedExperience.Id, TechnologyId = inactiveExperience.Id }, new ExperienceTechnology { ExperienceId = hiddenExperience.Id, TechnologyId = catalogOnly.Id });
        db.ProjectTechnologies.AddRange(new ProjectTechnology{ProjectId=project.Id,TechnologyId=projectUsed.Id},new ProjectTechnology{ProjectId=project.Id,TechnologyId=inactiveProject.Id},new ProjectTechnology{ProjectId=hiddenProject.Id,TechnologyId=catalogOnly.Id});
        await db.SaveChangesAsync();
        var snapshot = await new CandidateFactualSnapshotAssembler(db, new FixedTimeProvider(Now)).AssembleAsync();
        Assert.Contains("Used", snapshot.DemonstratedTechnologies); Assert.Contains("Project Used",snapshot.DemonstratedTechnologies); Assert.Contains("Skill Used", snapshot.DemonstratedTechnologies);
        Assert.Contains("Learning", snapshot.DevelopingTechnologies); Assert.Contains("Exploring", snapshot.DevelopingTechnologies); Assert.Contains("Skill Exploring", snapshot.DevelopingTechnologies);
        Assert.DoesNotContain("Exploring", snapshot.DemonstratedTechnologies); Assert.DoesNotContain("Inactive Experience", snapshot.DemonstratedTechnologies); Assert.DoesNotContain("Inactive Project", snapshot.DemonstratedTechnologies);
        Assert.DoesNotContain("Catalog only", snapshot.DemonstratedTechnologies); Assert.DoesNotContain("Hidden", snapshot.DemonstratedTechnologies);
        Assert.Single(snapshot.ExperienceIntervals);
    }

    [Fact]
    public async Task Fit_handler_is_read_only_and_preserves_job_workflow_state_and_version()
    {
        await using var db = PublicPortfolioTests.CreateContext(); var job = Job("Backend Developer", "Hanoi", null, null, ["C#"]); db.JobPostings.Add(job); await db.SaveChangesAsync();
        var saves = db.ChangeTracker.Entries().Count();
        var result = await new GetJobFitAnalysisQueryHandler(db, new StubAssembler(new(["C#"], [], [])), Scorer()).HandleAsync(new(job.Id));
        Assert.Equal(job.Version, result.JobVersion); Assert.Equal("PENDING_ANALYSIS", job.SelectionStatus); Assert.Equal("PENDING", job.VerificationStatus);
        Assert.Equal(saves, db.ChangeTracker.Entries().Count()); Assert.False(db.ChangeTracker.HasChanges());
        await Assert.ThrowsAsync<NotFoundException>(() => new GetJobFitAnalysisQueryHandler(db, new StubAssembler(new([], [], [])), Scorer()).HandleAsync(new(Guid.NewGuid())));
    }

    [Fact]
    public async Task Current_experience_ends_at_injected_today()
    {
        await using var db=PublicPortfolioTests.CreateContext();var current=Experience(true);current.StartDate=new DateOnly(2026,1,1);current.EndDate=null;current.IsCurrent=true;db.Experiences.Add(current);await db.SaveChangesAsync();
        var snapshot=await new CandidateFactualSnapshotAssembler(db,new FixedTimeProvider(Now)).AssembleAsync();Assert.Equal(new DateOnly(2026,9,21),Assert.Single(snapshot.ExperienceIntervals).End);
    }

    [Fact]
    public async Task Non_current_experience_without_end_date_is_excluded()
    {
        await using var db = PublicPortfolioTests.CreateContext();
        var incomplete = Experience(true); incomplete.EndDate = null; incomplete.IsCurrent = false;
        db.Experiences.Add(incomplete); await db.SaveChangesAsync();

        var snapshot = await new CandidateFactualSnapshotAssembler(db, new FixedTimeProvider(Now)).AssembleAsync();

        Assert.Empty(snapshot.ExperienceIntervals);
    }

    [Fact]
    public void Actual_appsettings_aliases_accept_equivalent_normalized_duplicates_and_score_workplace()
    {
        var options = ReadActualAppSettings();
        var validation = new JobFitScoringOptionsValidator().Validate(null, options);
        Assert.True(validation.Succeeded, string.Join(Environment.NewLine, validation.Failures ?? []));

        var result = new JobFitScorer(Options.Create(options)).Score(
            Job("Role", "Hanoi", "on-site", null, []), Preferences(workplace: ["on site"]), new([], [], []));

        Assert.Equal("MATCH", Component(result, "WORKPLACE_TYPE").Status);
    }

    [Fact]
    public void Conflicting_normalized_aliases_fail_options_validation()
    {
        var options = new JobFitScoringOptions
        {
            WorkplaceAliases = new(StringComparer.Ordinal) { ["on-site"] = "onsite", ["on site"] = "remote" }
        };

        var result = new JobFitScoringOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("WorkplaceAliases", StringComparison.Ordinal));
        Assert.Throws<OptionsValidationException>(() => new JobFitScorer(Options.Create(options)));
    }

    private static JobFitComponentResult Component(JobFitAnalysisResult result, string key) => result.Components.Single(x => x.Key == key);
    private static JobFitScorer Scorer() => new(Options.Create(new JobFitScoringOptions
    {
        TechnologyAliases = new(StringComparer.OrdinalIgnoreCase) { [".net"] = "dotnet", ["postgres"] = "postgresql" },
        RoleAliases = new(StringComparer.OrdinalIgnoreCase) { ["backend engineer"] = "backend developer" },
        WorkplaceAliases = new(StringComparer.OrdinalIgnoreCase) { ["work from home"] = "remote", ["on-site"] = "onsite" },
        EmploymentAliases = new(StringComparer.OrdinalIgnoreCase) { ["full-time"] = "full time" }
    }));
    private static CandidateJobPreferencesResult Preferences(string[]? targets = null, string[]? preferred = null, string[]? locations = null, string[]? workplace = null, string[]? employment = null, decimal? salary = null) =>
        new(Guid.NewGuid(), targets ?? [], preferred ?? [], locations ?? [], workplace ?? [], employment ?? [], salary, salary.HasValue ? "USD" : null, salary.HasValue ? "MONTH" : null, 1, Now, Now);
    private static JobPosting Job(string role, string location, string? workplace, string? employment, string[] technologies) => new()
    { Id = Guid.NewGuid(), CompanyName = "Acme", PositionTitle = role, Location = location, WorkplaceType = workplace, EmploymentType = employment, Description = "Description", TechnologyStack = JsonDocument.Parse(JsonSerializer.Serialize(technologies)), VerificationStatus = "PENDING", SelectionStatus = "PENDING_ANALYSIS", Version = 4, CreatedAt = Now, UpdatedAt = Now };
    private static UpdateCandidateJobPreferencesCommand Command(int version, string[] targets) => new(version, targets, ["C#"], ["Hanoi"], ["remote"], ["full time"], 1000, "usd", "month");
    private static Technology Tech(string name) => new() { Id = Guid.NewGuid(), Name = name, Category = "Tool", IsActive = true };
    private static Experience Experience(bool published) => new() { Id = Guid.NewGuid(), CompanyName = "Company", RoleTitle = "Role", StartDate = new(2024, 1, 1), EndDate = new(2024, 12, 31), IsPublished = published };
    private sealed class StubAssembler(CandidateFactualSnapshot snapshot) : ICandidateFactualSnapshotAssembler { public Task<CandidateFactualSnapshot> AssembleAsync(CancellationToken cancellationToken = default) => Task.FromResult(snapshot); }

    private static JobFitScoringOptions ReadActualAppSettings()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Portfolio.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory!.FullName, "src", "Portfolio.Api", "appsettings.json")));
        return JsonSerializer.Deserialize<JobFitScoringOptions>(document.RootElement.GetProperty(JobFitScoringOptions.SectionName).GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
}
