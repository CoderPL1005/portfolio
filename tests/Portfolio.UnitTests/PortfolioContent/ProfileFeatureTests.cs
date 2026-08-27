using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Features.Profile.GetAdminProfile;
using Portfolio.Application.Features.Profile.UpdateProfile;
using Portfolio.Domain.Entities;
using Portfolio.UnitTests.Authentication;

namespace Portfolio.UnitTests.PortfolioContent;

public sealed class ProfileFeatureTests
{
    [Fact]
    public async Task Admin_get_and_update_preserve_singleton_without_exposing_key()
    {
        await using var context = PublicPortfolioTests.CreateContext();
        var profile = new Profile { Id = Guid.NewGuid(), SingletonKey = 1, FullName = "Before", IsPublished = false };
        context.Profiles.Add(profile); await context.SaveChangesAsync();

        var before = await new GetAdminProfileQueryHandler(context).HandleAsync(new());
        var updated = await new UpdateProfileCommandHandler(
            context, new FixedTimeProvider(DateTimeOffset.UtcNow)).HandleAsync(new UpdateProfileCommand(
                "After", null, null, null, null, null, null, null, null, null, null,
                null, null, null, true));

        Assert.Equal("Before", before.FullName); Assert.Equal("After", updated.FullName);
        Assert.Equal(1, await context.Profiles.CountAsync());
        Assert.Equal(profile.Id, updated.Id);
        Assert.DoesNotContain(updated.GetType().GetProperties(), property => property.Name == "SingletonKey");
    }

    [Fact]
    public async Task Profile_validator_rejects_oversized_and_invalid_email()
    {
        var command = new UpdateProfileCommand(new string('x', 256), null, null, null, null, null,
            "not-email", null, null, null, null, null, null, null, true);
        var failures = await new UpdateProfileCommandValidator().ValidateAsync(command);
        Assert.Contains(failures, item => item.PropertyName == "fullName");
        Assert.Contains(failures, item => item.PropertyName == "email");
    }
}
