using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Features.PortfolioContent;

namespace Portfolio.Application.Features.Experiences;

public sealed class CreateExperienceCommandValidator : IRequestValidator<CreateExperienceCommand>
{
    public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(CreateExperienceCommand request, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<ValidationFailure>>(ExperienceValidation.Validate(
            request.CompanyName, request.RoleTitle, request.Location, request.StartDate,
            request.EndDate, request.IsCurrent, request.CompanyUrl, request.DisplayOrder,
            request.TechnologyIds));
}

public sealed class UpdateExperienceCommandValidator : IRequestValidator<UpdateExperienceCommand>
{
    public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(UpdateExperienceCommand request, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<ValidationFailure>>(ExperienceValidation.Validate(
            request.CompanyName, request.RoleTitle, request.Location, request.StartDate,
            request.EndDate, request.IsCurrent, request.CompanyUrl, request.DisplayOrder,
            request.TechnologyIds));
}

public sealed class ReorderExperiencesCommandValidator : IRequestValidator<ReorderExperiencesCommand>
{
    public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(ReorderExperiencesCommand request, CancellationToken cancellationToken = default)
    {
        var failures = new List<ValidationFailure>();
        ContentValidation.ReorderItems(failures, request.Items);
        return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(failures);
    }
}

internal static class ExperienceValidation
{
    public static List<ValidationFailure> Validate(
        string companyName, string roleTitle, string? location, DateOnly startDate,
        DateOnly? endDate, bool isCurrent, string? companyUrl, int displayOrder,
        IReadOnlyCollection<Guid> technologyIds)
    {
        var failures = new List<ValidationFailure>();
        ContentValidation.RequiredText(failures, "companyName", companyName, 255);
        ContentValidation.RequiredText(failures, "roleTitle", roleTitle, 255);
        ContentValidation.OptionalText(failures, "location", location, 255);
        ContentValidation.DateRange(failures, "endDate", startDate, endDate);
        if (isCurrent && endDate.HasValue)
            failures.Add(new("endDate", "A current experience cannot have an end date."));
        ContentValidation.HttpUrl(failures, "companyUrl", companyUrl);
        ContentValidation.DisplayOrder(failures, displayOrder);
        if (technologyIds.Count != technologyIds.Distinct().Count())
            failures.Add(new("technologyIds", "Technology IDs must be unique."));
        return failures;
    }
}
