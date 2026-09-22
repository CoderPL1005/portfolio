using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Abstractions.Storage;
using Portfolio.Application.Common.Abstractions.Validation;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Domain.Entities;

namespace Portfolio.Application.Features.JobHunting;

public sealed record CanonicalCvResult(
    bool IsConfigured, Guid? Id, string? FileName, string? ContentType,
    long? FileSizeBytes, string? ContentHash, int Version,
    DateTimeOffset? CreatedAt, DateTimeOffset? UpdatedAt);

public sealed record CanonicalCvContentResult(byte[] Content, string FileName, string ContentType);
public sealed record GetCanonicalCvQuery : IRequest<CanonicalCvResult>;
public sealed record GetCanonicalCvContentQuery : IRequest<CanonicalCvContentResult>;
public sealed record UploadCanonicalCvCommand(
    Stream? Content, string? FileName, string? ContentType, long FileSize, int ExpectedVersion)
    : IRequest<CanonicalCvResult>;

public sealed class GetCanonicalCvQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetCanonicalCvQuery, CanonicalCvResult>
{
    public async Task<CanonicalCvResult> HandleAsync(GetCanonicalCvQuery request, CancellationToken cancellationToken = default)
    {
        var entity = await db.CanonicalCvs.AsNoTracking()
            .SingleOrDefaultAsync(item => item.SingletonKey == CanonicalCvRules.SingletonKey, cancellationToken);
        return entity is null ? CanonicalCvRules.Missing : CanonicalCvRules.Map(entity);
    }
}

public sealed class GetCanonicalCvContentQueryHandler(
    IApplicationDbContext db,
    IPrivateFileStorage storage,
    ILogger<GetCanonicalCvContentQueryHandler> logger)
    : IRequestHandler<GetCanonicalCvContentQuery, CanonicalCvContentResult>
{
    public async Task<CanonicalCvContentResult> HandleAsync(
        GetCanonicalCvContentQuery request, CancellationToken cancellationToken = default)
    {
        var entity = await db.CanonicalCvs.AsNoTracking()
            .SingleOrDefaultAsync(item => item.SingletonKey == CanonicalCvRules.SingletonKey, cancellationToken)
            ?? throw new NotFoundException("CANONICAL_CV_NOT_FOUND", "The canonical CV has not been uploaded.");
        try
        {
            await using var source = await storage.OpenReadAsync(
                entity.StorageKey, CanonicalCvRules.MaxFileBytes, cancellationToken);
            using var target = new MemoryStream((int)entity.FileSizeBytes);
            await source.CopyToAsync(target, cancellationToken);
            var bytes = target.ToArray();
            var hash = CanonicalCvRules.Hash(bytes);
            if (bytes.LongLength != entity.FileSizeBytes
                || !hash.Equals(entity.ContentHash, StringComparison.Ordinal))
            {
                logger.LogError("Canonical CV {CanonicalCvId} failed its stored integrity check.", entity.Id);
                throw new ServiceUnavailableException(
                    "CANONICAL_CV_STORAGE_INVALID", "The canonical CV could not be read safely.");
            }
            return new(bytes, entity.FileName, entity.ContentType);
        }
        catch (ServiceUnavailableException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Private canonical CV read failed for {CanonicalCvId}.", entity.Id);
            throw new ServiceUnavailableException(
                "CANONICAL_CV_STORAGE_UNAVAILABLE", "The canonical CV is temporarily unavailable.");
        }
    }
}

public sealed class UploadCanonicalCvCommandHandler(
    IApplicationDbContext db,
    IPrivateFileStorage storage,
    ICanonicalCvConflictDetector conflictDetector,
    TimeProvider clock,
    ILogger<UploadCanonicalCvCommandHandler> logger)
    : IRequestHandler<UploadCanonicalCvCommand, CanonicalCvResult>
{
    public async Task<CanonicalCvResult> HandleAsync(
        UploadCanonicalCvCommand request, CancellationToken cancellationToken = default)
    {
        var file = await CanonicalCvRules.ReadAndValidateAsync(request, cancellationToken);
        var entity = await db.CanonicalCvs
            .SingleOrDefaultAsync(item => item.SingletonKey == CanonicalCvRules.SingletonKey, cancellationToken);
        if ((entity is null && request.ExpectedVersion != 0)
            || (entity is not null && entity.Version != request.ExpectedVersion))
        {
            throw VersionConflict();
        }

        var newStorageKey = $"canonical-cv/{Guid.NewGuid():N}.pdf";
        await using (var upload = new MemoryStream(file.Content, writable: false))
        {
            await storage.UploadAsync(newStorageKey, upload, CanonicalCvRules.PdfContentType, cancellationToken);
        }

        var previousStorageKey = entity?.StorageKey;
        var now = clock.GetUtcNow();
        var creating = entity is null;
        if (creating)
        {
            entity = new CanonicalCv
            {
                Id = Guid.NewGuid(),
                SingletonKey = CanonicalCvRules.SingletonKey,
                Version = 1,
                CreatedAt = now,
            };
            db.CanonicalCvs.Add(entity);
        }
        else
        {
            entity!.Version++;
        }
        entity.StorageKey = newStorageKey;
        entity.FileName = file.FileName;
        entity.ContentType = CanonicalCvRules.PdfContentType;
        entity.FileSizeBytes = file.Content.LongLength;
        entity.ContentHash = file.ContentHash;
        entity.UpdatedAt = now;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await DeleteCompensationAsync(newStorageKey);
            throw VersionConflict();
        }
        catch (DbUpdateException exception) when (creating && conflictDetector.IsSingletonConflict(exception))
        {
            await DeleteCompensationAsync(newStorageKey);
            throw VersionConflict();
        }
        catch
        {
            await DeleteCompensationAsync(newStorageKey);
            throw;
        }

        if (previousStorageKey is not null)
        {
            try
            {
                await storage.DeleteAsync(previousStorageKey, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception,
                    "Superseded private canonical CV cleanup failed for {CanonicalCvId} version {CanonicalCvVersion}.",
                    entity.Id, entity.Version - 1);
            }
        }
        return CanonicalCvRules.Map(entity);
    }

    private async Task DeleteCompensationAsync(string storageKey)
    {
        try { await storage.DeleteAsync(storageKey, CancellationToken.None); }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Canonical CV upload compensation failed.");
        }
    }

    private static ConflictException VersionConflict() => new(
        "CANONICAL_CV_VERSION_CONFLICT", "The canonical CV was changed by another request. Refresh and try again.");
}

public sealed class UploadCanonicalCvCommandValidator : IRequestValidator<UploadCanonicalCvCommand>
{
    public Task<IReadOnlyCollection<ValidationFailure>> ValidateAsync(
        UploadCanonicalCvCommand request, CancellationToken cancellationToken = default)
    {
        var failures = new List<ValidationFailure>();
        if (request.ExpectedVersion < 0)
            failures.Add(new("expectedVersion", "expectedVersion must be zero or greater."));
        if (request.Content is null || request.FileSize <= 0)
            failures.Add(new("file", "A PDF file is required."));
        else if (request.FileSize > CanonicalCvRules.MaxFileBytes)
            failures.Add(new("file", $"The canonical CV must not exceed {CanonicalCvRules.MaxFileBytes} bytes."));
        if (!string.Equals(request.ContentType, CanonicalCvRules.PdfContentType, StringComparison.OrdinalIgnoreCase))
            failures.Add(new("file", "Only PDF files are allowed."));
        return Task.FromResult<IReadOnlyCollection<ValidationFailure>>(failures);
    }
}

internal static class CanonicalCvRules
{
    public const short SingletonKey = 1;
    public const long MaxFileBytes = 10 * 1024 * 1024;
    public const string PdfContentType = "application/pdf";
    public static CanonicalCvResult Missing { get; } = new(false, null, null, null, null, null, 0, null, null);

    public static CanonicalCvResult Map(CanonicalCv entity) => new(
        true, entity.Id, entity.FileName, entity.ContentType, entity.FileSizeBytes,
        entity.ContentHash, entity.Version, entity.CreatedAt, entity.UpdatedAt);

    public static async Task<ValidatedCanonicalCv> ReadAndValidateAsync(
        UploadCanonicalCvCommand request, CancellationToken cancellationToken)
    {
        var failures = await new UploadCanonicalCvCommandValidator().ValidateAsync(request, cancellationToken);
        if (failures.Count > 0) throw new ValidationException(failures);

        using var target = new MemoryStream((int)Math.Min(request.FileSize, MaxFileBytes));
        var buffer = new byte[81920];
        while (true)
        {
            var read = await request.Content!.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (target.Length + read > MaxFileBytes)
                throw Invalid("The canonical CV exceeds the permitted file size.");
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        var bytes = target.ToArray();
        if (bytes.LongLength != request.FileSize)
            throw Invalid("The canonical CV reported size does not match its content.");
        if (bytes.Length < 5 || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
            throw Invalid("The uploaded file does not contain a valid PDF signature.");
        return new(bytes, SafeFileName(request.FileName), Hash(bytes));
    }

    public static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string SafeFileName(string? value)
    {
        var leaf = (value ?? string.Empty).Replace('\\', '/').Split('/').Last().Trim();
        var safe = new string(leaf.Select(character =>
            char.IsControl(character) || character is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*'
                ? '_'
                : character).ToArray()).Trim(' ', '.');
        if (safe.Length == 0) safe = "CV.pdf";
        if (!safe.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) safe += ".pdf";
        if (safe.Length > 255) safe = safe[..251] + ".pdf";
        return safe;
    }

    private static ValidationException Invalid(string message) => new([new("file", message)]);
}

internal sealed record ValidatedCanonicalCv(byte[] Content, string FileName, string ContentHash);
