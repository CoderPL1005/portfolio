using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.Certificates;
using Portfolio.Application.Features.Education;
using Portfolio.Application.Features.Experiences;
using Portfolio.Application.Features.Trainings;
using Portfolio.Domain.Entities;
using Portfolio.UnitTests.Authentication;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class ContentCrudTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Experience_create_update_list_reorder_and_delete_work()
    {
        await using var context = PublicPortfolioTests.CreateContext();
        var technology = new Technology { Id = Guid.NewGuid(), Name = "C#", Category = "Backend", IsActive = true };
        context.Technologies.Add(technology); await context.SaveChangesAsync();
        var createHandler = new CreateExperienceCommandHandler(context, new FixedTimeProvider(Now));
        var created = await createHandler.HandleAsync(new CreateExperienceCommand(
            "Company", "Role", null, new DateOnly(2025, 1, 1), null, true,
            "Summary", null, "https://example.com", 2, false, [technology.Id]));
        var other = new Experience { Id = Guid.NewGuid(), CompanyName = "Other", RoleTitle = "Role", StartDate = new DateOnly(2024, 1, 1), DisplayOrder = 1, IsPublished = true };
        context.Experiences.Add(other); await context.SaveChangesAsync();

        var listed = await new GetExperiencesQueryHandler(context).HandleAsync(new("company"));
        var updated = await new UpdateExperienceCommandHandler(context, new FixedTimeProvider(Now)).HandleAsync(
            new UpdateExperienceCommand(created.Id, "Updated", "Role", null,
                new DateOnly(2025, 1, 1), new DateOnly(2025, 2, 1), false,
                null, null, null, 2, true, []));
        await new ReorderExperiencesCommandHandler(context).HandleAsync(new(
            [new(created.Id, 0), new(other.Id, 1)]));
        await new DeleteExperienceCommandHandler(context).HandleAsync(new(created.Id));

        Assert.Single(listed); Assert.Single(listed.Single().Technologies);
        Assert.Equal("Updated", updated.CompanyName);
        Assert.Empty(await context.ExperienceTechnologies.Where(item => item.ExperienceId == created.Id).ToListAsync());
        Assert.False(await context.Experiences.AnyAsync(item => item.Id == created.Id));
    }

    [Fact]
    public async Task Experience_missing_id_and_invalid_input_are_rejected()
    {
        await using var context = PublicPortfolioTests.CreateContext();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            new GetExperienceQueryHandler(context).HandleAsync(new(Guid.NewGuid())));
        var failures = await new CreateExperienceCommandValidator().ValidateAsync(
            new CreateExperienceCommand(new string('x', 256), "Role", null,
                new DateOnly(2025, 2, 1), new DateOnly(2025, 1, 1), true,
                null, null, "javascript:alert(1)", -1, true, []));
        Assert.Contains(failures, item => item.PropertyName == "companyName");
        Assert.Contains(failures, item => item.PropertyName == "endDate");
        Assert.Contains(failures, item => item.PropertyName == "companyUrl");
        Assert.Contains(failures, item => item.PropertyName == "displayOrder");
    }

    [Fact]
    public async Task Education_complete_crud_and_not_found_behavior_work()
    {
        await using var context = PublicPortfolioTests.CreateContext();
        var created = await new CreateEducationCommandHandler(context, new FixedTimeProvider(Now)).HandleAsync(
            new CreateEducationCommand("School", "Degree", "Field", null, null, null, null, 1, false));
        var updated = await new UpdateEducationCommandHandler(context, new FixedTimeProvider(Now)).HandleAsync(
            new UpdateEducationCommand(created.Id, "Updated School", null, null, null, null, null, null, 0, true));
        Assert.Single(await new GetEducationsQueryHandler(context).HandleAsync(new()));
        Assert.Equal("Updated School", updated.Institution);
        await new DeleteEducationCommandHandler(context).HandleAsync(new(created.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => new GetEducationQueryHandler(context).HandleAsync(new(created.Id)));
    }

    [Fact]
    public async Task Education_list_orders_entities_by_display_order_then_id_before_projection()
    {
        await using var context = PublicPortfolioTests.CreateContext();
        var firstId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var laterId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        context.Educations.AddRange(
            new Education { Id = laterId, Institution = "Later", DisplayOrder = 2 },
            new Education { Id = secondId, Institution = "Second tie", DisplayOrder = 1 },
            new Education { Id = firstId, Institution = "First tie", DisplayOrder = 1 });
        await context.SaveChangesAsync();

        var result = await new GetEducationsQueryHandler(context).HandleAsync(new());

        Assert.Equal([firstId, secondId, laterId], result.Select(item => item.Id));
    }

    [Fact]
    public async Task Training_complete_crud_and_url_validation_work()
    {
        await using var context = PublicPortfolioTests.CreateContext();
        var created = await new CreateTrainingCommandHandler(context, new FixedTimeProvider(Now)).HandleAsync(
            new CreateTrainingCommand("Course", "Provider", null, null, null, "https://example.com", 1, true));
        var updated = await new UpdateTrainingCommandHandler(context, new FixedTimeProvider(Now)).HandleAsync(
            new UpdateTrainingCommand(created.Id, "Updated", null, null, null, null, null, 0, false));
        Assert.Equal("Updated", updated.Title);
        Assert.Single(await new GetTrainingsQueryHandler(context).HandleAsync(new()));
        var failures = await new CreateTrainingCommandValidator().ValidateAsync(
            new CreateTrainingCommand("", null, null, new DateOnly(2025, 2, 1),
                new DateOnly(2025, 1, 1), "data:text/plain,bad", -1, true));
        Assert.True(failures.Count >= 4);
        await new DeleteTrainingCommandHandler(context).HandleAsync(new(created.Id));
    }

    [Fact]
    public async Task Training_list_orders_entities_by_display_order_then_id_before_projection()
    {
        await using var context = PublicPortfolioTests.CreateContext();
        var firstId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var laterId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        context.Trainings.AddRange(
            new Training { Id = laterId, Title = "Later", DisplayOrder = 2 },
            new Training { Id = secondId, Title = "Second tie", DisplayOrder = 1 },
            new Training { Id = firstId, Title = "First tie", DisplayOrder = 1 });
        await context.SaveChangesAsync();

        var result = await new GetTrainingsQueryHandler(context).HandleAsync(new());

        Assert.Equal([firstId, secondId, laterId], result.Select(item => item.Id));
    }

    [Fact]
    public async Task Certificate_admin_crud_includes_unpublished_records_and_validates_dates()
    {
        await using var context = PublicPortfolioTests.CreateContext();
        var created = await new CreateCertificateCommandHandler(context, new FixedTimeProvider(Now)).HandleAsync(
            new CreateCertificateCommand("Hidden", "Issuer", null, null, null, null, null, 1, false));
        var adminList = await new GetCertificatesQueryHandler(context).HandleAsync(new());
        var updated = await new UpdateCertificateCommandHandler(context, new FixedTimeProvider(Now)).HandleAsync(
            new UpdateCertificateCommand(created.Id, "Published", null, null, null, null, null, null, 0, true));
        var failures = await new CreateCertificateCommandValidator().ValidateAsync(
            new CreateCertificateCommand("Name", null, new DateOnly(2025, 2, 1),
                new DateOnly(2025, 1, 1), null, null, null, 0, true));
        Assert.Single(adminList); Assert.False(adminList.Single().IsPublished);
        Assert.True(updated.IsPublished);
        Assert.Contains(failures, item => item.PropertyName == "expiresAt");
        await new DeleteCertificateCommandHandler(context).HandleAsync(new(created.Id));
    }

    [Fact]
    public async Task Certificate_list_orders_entities_by_display_order_then_id_before_projection()
    {
        await using var context = PublicPortfolioTests.CreateContext();
        var firstId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var laterId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        context.Certificates.AddRange(
            new Certificate { Id = laterId, Name = "Later", DisplayOrder = 2 },
            new Certificate { Id = secondId, Name = "Second tie", DisplayOrder = 1 },
            new Certificate { Id = firstId, Name = "First tie", DisplayOrder = 1 });
        await context.SaveChangesAsync();

        var result = await new GetCertificatesQueryHandler(context).HandleAsync(new());

        Assert.Equal([firstId, secondId, laterId], result.Select(item => item.Id));
    }

    [Fact]
    public async Task Education_training_and_certificate_reorder_handlers_are_deterministic()
    {
        await using var context = PublicPortfolioTests.CreateContext();
        var education = new Education { Id = Guid.NewGuid(), Institution = "School", DisplayOrder = 9 };
        var training = new Training { Id = Guid.NewGuid(), Title = "Course", DisplayOrder = 9 };
        var certificate = new Certificate { Id = Guid.NewGuid(), Name = "Cert", DisplayOrder = 9 };
        context.AddRange(education, training, certificate); await context.SaveChangesAsync();

        await new ReorderEducationsCommandHandler(context).HandleAsync(new([new(education.Id, 1)]));
        await new ReorderTrainingsCommandHandler(context).HandleAsync(new([new(training.Id, 2)]));
        await new ReorderCertificatesCommandHandler(context).HandleAsync(new([new(certificate.Id, 3)]));

        Assert.Equal(1, education.DisplayOrder); Assert.Equal(2, training.DisplayOrder);
        Assert.Equal(3, certificate.DisplayOrder);
    }

    [Fact]
    public async Task Education_validator_rejects_oversized_fields_invalid_dates_and_order()
    {
        var failures = await new CreateEducationCommandValidator().ValidateAsync(
            new CreateEducationCommand(new string('x', 256), null, null,
                new DateOnly(2025, 2, 1), new DateOnly(2025, 1, 1), null, null, -1, true));
        Assert.Contains(failures, item => item.PropertyName == "institution");
        Assert.Contains(failures, item => item.PropertyName == "endDate");
        Assert.Contains(failures, item => item.PropertyName == "displayOrder");
    }

    [Fact]
    public async Task Missing_related_resources_and_missing_content_return_contract_not_found_codes()
    {
        await using var context = PublicPortfolioTests.CreateContext();

        var technologyError = await Assert.ThrowsAsync<NotFoundException>(() =>
            new CreateExperienceCommandHandler(context, new FixedTimeProvider(Now)).HandleAsync(
                new CreateExperienceCommand("Company", "Role", null, new DateOnly(2025, 1, 1),
                    null, true, null, null, null, 0, true, [Guid.NewGuid()])));
        var mediaError = await Assert.ThrowsAsync<NotFoundException>(() =>
            new CreateCertificateCommandHandler(context, new FixedTimeProvider(Now)).HandleAsync(
                new CreateCertificateCommand("Certificate", null, null, null, null, null,
                    Guid.NewGuid(), 0, true)));
        var trainingError = await Assert.ThrowsAsync<NotFoundException>(() =>
            new GetTrainingQueryHandler(context).HandleAsync(new(Guid.NewGuid())));
        var certificateError = await Assert.ThrowsAsync<NotFoundException>(() =>
            new DeleteCertificateCommandHandler(context).HandleAsync(new(Guid.NewGuid())));

        Assert.Equal("TECHNOLOGY_NOT_FOUND", technologyError.Code);
        Assert.Equal("MEDIA_NOT_FOUND", mediaError.Code);
        Assert.Equal("TRAINING_NOT_FOUND", trainingError.Code);
        Assert.Equal("CERTIFICATE_NOT_FOUND", certificateError.Code);
    }

    [Fact]
    public async Task Reorder_validation_rejects_empty_duplicate_and_negative_items()
    {
        var id = Guid.NewGuid();
        var empty = await new ReorderExperiencesCommandValidator().ValidateAsync(new([]));
        var duplicate = await new ReorderEducationsCommandValidator().ValidateAsync(
            new([new(id, 0), new(id, 1)]));
        var negative = await new ReorderCertificatesCommandValidator().ValidateAsync(
            new([new(Guid.NewGuid(), -1)]));

        Assert.Contains(empty, item => item.PropertyName == "items");
        Assert.Contains(duplicate, item => item.PropertyName == "items");
        Assert.Contains(negative, item => item.PropertyName == "items");
    }
}
