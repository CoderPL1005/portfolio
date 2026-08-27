using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Features.Phase4C;
using Portfolio.Application.Features.PortfolioContent;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.SiteSettings;

public sealed record GetSiteSettingsQuery : IRequest<SiteSettingsResult>;
public sealed record UpdateSiteSettingsCommand(string SiteName, string? FooterText,
    bool ShowAvailability, bool EnableContactForm, bool ShowDownloadCv, bool ShowJourney,
    bool ShowAiAgent, string? DefaultSeoTitle, string? DefaultSeoDescription)
    : IRequest<SiteSettingsResult>;

public sealed class GetSiteSettingsQueryHandler(IApplicationDbContext db) : IRequestHandler<GetSiteSettingsQuery, SiteSettingsResult>
{ public async Task<SiteSettingsResult> HandleAsync(GetSiteSettingsQuery r, CancellationToken ct = default) => SiteSettingsMapping.Map(await db.SiteSettings.AsNoTracking().ToListAsync(ct)); }
public sealed class UpdateSiteSettingsCommandHandler(IApplicationDbContext db, TimeProvider clock) : IRequestHandler<UpdateSiteSettingsCommand, SiteSettingsResult>
{
    public async Task<SiteSettingsResult> HandleAsync(UpdateSiteSettingsCommand r, CancellationToken ct = default)
    {
        var existing = (await db.SiteSettings.ToListAsync(ct)).ToDictionary(x => x.Key, StringComparer.Ordinal); var now = clock.GetUtcNow();
        Set("siteName", r.SiteName.Trim()); Set("footerText", r.FooterText?.Trim()); Set("showAvailability", r.ShowAvailability); Set("enableContactForm", r.EnableContactForm); Set("showDownloadCv", r.ShowDownloadCv); Set("showJourney", r.ShowJourney); Set("showAiAgent", r.ShowAiAgent); Set("defaultSeoTitle", r.DefaultSeoTitle?.Trim()); Set("defaultSeoDescription", r.DefaultSeoDescription?.Trim());
        await db.SaveChangesAsync(ct); return new(r.SiteName.Trim(), r.FooterText?.Trim(), r.ShowAvailability, r.EnableContactForm, r.ShowDownloadCv, r.ShowJourney, r.ShowAiAgent, r.DefaultSeoTitle?.Trim(), r.DefaultSeoDescription?.Trim());
        void Set<T>(string key, T value) { if (!existing.TryGetValue(key, out var row)) { row = new SiteSetting { Key = key }; db.SiteSettings.Add(row); existing[key] = row; } row.Value = JsonSerializer.SerializeToDocument(value); row.UpdatedAt = now; }
    }
}
public sealed class UpdateSiteSettingsCommandValidator : IRequestValidator<UpdateSiteSettingsCommand>
{
    public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(UpdateSiteSettingsCommand r, CancellationToken ct = default)
    { var f = new List<ValidationFailure>(); ContentValidation.RequiredText(f, "siteName", r.SiteName, 255); ContentValidation.OptionalText(f, "footerText", r.FooterText, 500); ContentValidation.OptionalText(f, "defaultSeoTitle", r.DefaultSeoTitle, 255); ContentValidation.OptionalText(f, "defaultSeoDescription", r.DefaultSeoDescription, 500); return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(f); }
}
internal static class SiteSettingsMapping
{
    public static SiteSettingsResult Map(IReadOnlyCollection<SiteSetting> rows)
    { var map = rows.ToDictionary(x => x.Key, x => x.Value.RootElement, StringComparer.Ordinal); return new(GetString("siteName") ?? string.Empty, GetString("footerText"), GetBool("showAvailability"), GetBool("enableContactForm"), GetBool("showDownloadCv"), GetBool("showJourney"), GetBool("showAiAgent"), GetString("defaultSeoTitle"), GetString("defaultSeoDescription")); string? GetString(string key) => map.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null; bool GetBool(string key) => map.TryGetValue(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean(); }
}
