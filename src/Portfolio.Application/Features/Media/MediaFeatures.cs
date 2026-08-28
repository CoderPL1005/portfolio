using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Storage;
using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Common.Models;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.Media;

public sealed record MediaAssetResult(Guid Id, string FileName, string? MimeType, long? FileSize, string MediaType, string StorageKey, string PublicUrl, string? AltText, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record GetMediaQuery(int Page = 1, int PageSize = 20, string? MediaType = null, string? Search = null) : IRequest<PagedResult<MediaAssetResult>>;
public sealed record UploadMediaCommand(Stream Content, string FileName, string ContentType, long FileSize, string MediaType, string? AltText) : IRequest<MediaAssetResult>;
public sealed record UpdateMediaCommand(Guid Id, string? AltText, string MediaType) : IRequest<MediaAssetResult>;
public sealed record DeleteMediaCommand(Guid Id) : IRequest<bool>;

public sealed class GetMediaQueryValidator : IRequestValidator<GetMediaQuery>
{
    public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(GetMediaQuery r, CancellationToken ct = default)
    { var e = new List<ValidationFailure>(); if (r.Page < 1) e.Add(new(nameof(r.Page), "Page must be at least 1.")); if (r.PageSize is < 1 or > 100) e.Add(new(nameof(r.PageSize), "Page size must be between 1 and 100.")); if (r.MediaType is not null && !MediaRules.Types.Contains(r.MediaType.Trim().ToUpperInvariant())) e.Add(new(nameof(r.MediaType), "Media type is invalid.")); return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(e); }
}
public sealed class UpdateMediaCommandValidator : IRequestValidator<UpdateMediaCommand>
{
    public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(UpdateMediaCommand r, CancellationToken ct = default) => Task.FromResult<IReadOnlyCollection<ValidationFailure>>(MediaRules.MetadataFailures(r.MediaType, r.AltText));
}
public sealed class UploadMediaCommandValidator : IRequestValidator<UploadMediaCommand>
{
    public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(UploadMediaCommand r, CancellationToken ct = default)
    { var e=MediaRules.MetadataFailures(r.MediaType,r.AltText);if(string.IsNullOrWhiteSpace(r.FileName)||Path.GetFileName(r.FileName).Length>255)e.Add(new(nameof(r.FileName),"File name is required and must not exceed 255 characters."));if(r.FileSize is <=0 or > MediaRules.MaxBytes)e.Add(new(nameof(r.FileSize),$"File size must be between 1 and {MediaRules.MaxBytes} bytes."));if(!MediaRules.IsAllowedMime(r.ContentType))e.Add(new(nameof(r.ContentType),"Only JPEG, PNG, WebP, GIF, and PDF files are allowed."));return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(e); }
}

public sealed class GetMediaQueryHandler(IApplicationDbContext db) : IRequestHandler<GetMediaQuery, PagedResult<MediaAssetResult>>
{
    public async Task<PagedResult<MediaAssetResult>> HandleAsync(GetMediaQuery r, CancellationToken ct = default)
    {
        var q = db.MediaAssets.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(r.MediaType)) { var type = r.MediaType.Trim().ToUpperInvariant(); q = q.Where(x => x.MediaType == type); }
        if (!string.IsNullOrWhiteSpace(r.Search)) { var search = r.Search.Trim().ToLower(); q = q.Where(x => x.FileName.ToLower().Contains(search) || (x.AltText != null && x.AltText.ToLower().Contains(search))); }
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip((r.Page - 1) * r.PageSize).Take(r.PageSize).Select(MediaRules.Project).ToListAsync(ct);
        return new(items, r.Page, r.PageSize, total);
    }
}

public sealed class UploadMediaCommandHandler(IApplicationDbContext db, IFileStorage storage, TimeProvider clock) : IRequestHandler<UploadMediaCommand, MediaAssetResult>
{
    public async Task<MediaAssetResult> HandleAsync(UploadMediaCommand r, CancellationToken ct = default)
    {
        await MediaRules.ValidateUploadAsync(r, ct);
        var now = clock.GetUtcNow(); var key = $"portfolio/{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{MediaRules.Extension(r.ContentType)}";
        var url = await storage.UploadAsync(key, r.Content, r.ContentType, ct);
        var entity = new MediaAsset { Id = Guid.NewGuid(), StorageKey = key, PublicUrl = url, FileName = Path.GetFileName(r.FileName), MimeType = r.ContentType, FileSize = r.FileSize, AltText = MediaRules.Trim(r.AltText), MediaType = r.MediaType.Trim().ToUpperInvariant(), CreatedAt = now, UpdatedAt = now };
        try { db.MediaAssets.Add(entity); await db.SaveChangesAsync(ct); }
        catch { try { await storage.DeleteAsync(key, ct); } catch { } throw; }
        return MediaRules.Map(entity);
    }
}

public sealed class UpdateMediaCommandHandler(IApplicationDbContext db, TimeProvider clock) : IRequestHandler<UpdateMediaCommand, MediaAssetResult>
{
    public async Task<MediaAssetResult> HandleAsync(UpdateMediaCommand r, CancellationToken ct = default)
    {
        var x = await db.MediaAssets.SingleOrDefaultAsync(x => x.Id == r.Id, ct) ?? throw new NotFoundException("MEDIA_NOT_FOUND", "The media asset was not found.");
        var type = r.MediaType.Trim().ToUpperInvariant(); MediaRules.EnsureCompatibility(x.MimeType, type);
        x.AltText = MediaRules.Trim(r.AltText); x.MediaType = type; x.UpdatedAt = clock.GetUtcNow(); await db.SaveChangesAsync(ct); return MediaRules.Map(x);
    }
}

public sealed class DeleteMediaCommandHandler(IApplicationDbContext db, IFileStorage storage) : IRequestHandler<DeleteMediaCommand, bool>
{
    public async Task<bool> HandleAsync(DeleteMediaCommand r, CancellationToken ct = default)
    {
        var x = await db.MediaAssets.SingleOrDefaultAsync(x => x.Id == r.Id, ct) ?? throw new NotFoundException("MEDIA_NOT_FOUND", "The media asset was not found.");
        var used = await db.Profiles.AnyAsync(p => p.ProfileImageId == r.Id || p.CvMediaId == r.Id, ct) || await db.Projects.AnyAsync(p => p.ThumbnailMediaId == r.Id, ct) || await db.Certificates.AnyAsync(c => c.CertificateMediaId == r.Id, ct) || await db.ProjectMedia.AnyAsync(m => m.MediaAssetId == r.Id, ct);
        if (used) throw new ConflictException("MEDIA_IN_USE", "The media asset is referenced by portfolio content.");
        await storage.DeleteAsync(x.StorageKey, ct); db.MediaAssets.Remove(x); await db.SaveChangesAsync(ct); return true;
    }
}

internal static class MediaRules
{
    public const long MaxBytes = 10 * 1024 * 1024;
    public static readonly string[] Types = ["IMAGE", "DOCUMENT", "CV", "OTHER"];
    private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ["image/jpeg"] = ".jpg", ["image/png"] = ".png", ["image/webp"] = ".webp", ["image/gif"] = ".gif", ["application/pdf"] = ".pdf" };
    public static readonly System.Linq.Expressions.Expression<Func<MediaAsset, MediaAssetResult>> Project = x => new(x.Id, x.FileName, x.MimeType, x.FileSize, x.MediaType, x.StorageKey, x.PublicUrl, x.AltText, x.CreatedAt, x.UpdatedAt);
    public static MediaAssetResult Map(MediaAsset x) => new(x.Id, x.FileName, x.MimeType, x.FileSize, x.MediaType, x.StorageKey, x.PublicUrl, x.AltText, x.CreatedAt, x.UpdatedAt);
    public static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    public static string Extension(string contentType) => Extensions[contentType];
    public static bool IsAllowedMime(string contentType)=>Extensions.ContainsKey(contentType);
    public static List<ValidationFailure> MetadataFailures(string mediaType, string? altText) { var e = new List<ValidationFailure>(); if (!Types.Contains(mediaType?.Trim().ToUpperInvariant())) e.Add(new(nameof(mediaType), "Media type is invalid.")); if (altText?.Length > 500) e.Add(new(nameof(altText), "Alt text must not exceed 500 characters.")); return e; }
    public static async Task ValidateUploadAsync(UploadMediaCommand r, CancellationToken ct)
    {
        var e = MetadataFailures(r.MediaType, r.AltText); if (string.IsNullOrWhiteSpace(r.FileName) || Path.GetFileName(r.FileName).Length > 255) e.Add(new(nameof(r.FileName), "File name is required and must not exceed 255 characters.")); if (r.FileSize is <= 0 or > MaxBytes) e.Add(new(nameof(r.FileSize), $"File size must be between 1 and {MaxBytes} bytes.")); if (!Extensions.ContainsKey(r.ContentType)) e.Add(new(nameof(r.ContentType), "Only JPEG, PNG, WebP, GIF, and PDF files are allowed."));
        if (e.Count == 0) { EnsureCompatibility(r.ContentType, r.MediaType.Trim().ToUpperInvariant()); var header = new byte[12]; var read = await r.Content.ReadAsync(header, ct); if (r.Content.CanSeek) r.Content.Position = 0; else e.Add(new(nameof(r.Content), "The upload stream must support validation.")); if (!SignatureMatches(r.ContentType, header.AsSpan(0, read))) e.Add(new(nameof(r.Content), "File content does not match its declared type.")); }
        if (e.Count > 0) throw new ValidationException(e);
    }
    public static void EnsureCompatibility(string? mime, string type)
    { var ok = mime?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true ? type == "IMAGE" : mime == "application/pdf" && type is "DOCUMENT" or "CV" or "OTHER"; if (!ok) throw new ValidationException([new(nameof(type), "Media type is incompatible with the file content.")]); }
    private static bool SignatureMatches(string mime, ReadOnlySpan<byte> h) => mime switch { "image/jpeg" => h.Length >= 3 && h[0] == 0xff && h[1] == 0xd8 && h[2] == 0xff, "image/png" => h.StartsWith(new byte[] {0x89,0x50,0x4e,0x47,0x0d,0x0a,0x1a,0x0a}), "image/gif" => h.StartsWith("GIF87a"u8) || h.StartsWith("GIF89a"u8), "image/webp" => h.Length >= 12 && h[..4].SequenceEqual("RIFF"u8) && h[8..12].SequenceEqual("WEBP"u8), "application/pdf" => h.StartsWith("%PDF-"u8), _ => false };
}
