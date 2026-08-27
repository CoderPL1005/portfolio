using System.Net.Mail;
using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Features.PortfolioContent;

namespace Portfolio.Application.Features.Profile.UpdateProfile;

public sealed class UpdateProfileCommandValidator : IRequestValidator<UpdateProfileCommand>
{
    public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(
        UpdateProfileCommand request,
        CancellationToken cancellationToken = default)
    {
        var failures = new List<ValidationFailure>();
        ContentValidation.RequiredText(failures, "fullName", request.FullName, 255);
        ContentValidation.OptionalText(failures, "professionalTitle", request.ProfessionalTitle, 255);
        ContentValidation.OptionalText(failures, "secondaryTitle", request.SecondaryTitle, 255);
        ContentValidation.OptionalText(failures, "heroHeadline", request.HeroHeadline, 500);
        ContentValidation.OptionalText(failures, "email", request.Email, 255);
        ContentValidation.OptionalText(failures, "phone", request.Phone, 50);
        ContentValidation.OptionalText(failures, "location", request.Location, 255);
        ContentValidation.OptionalText(failures, "university", request.University, 255);
        ContentValidation.OptionalText(failures, "major", request.Major, 255);
        ContentValidation.OptionalText(failures, "availabilityStatus", request.AvailabilityStatus, 150);
        if (!string.IsNullOrWhiteSpace(request.Email) && !MailAddress.TryCreate(request.Email, out _))
        {
            failures.Add(new("email", "Email must be valid."));
        }

        return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(failures);
    }
}
