using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using Portfolio.Application.Common.Abstractions.Storage;

namespace Portfolio.Infrastructure.Storage;

public sealed class R2FileStorage(IOptions<R2Settings> options) : IFileStorage
{
    public async Task<string> UploadAsync(string storageKey, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var settings = RequireSettings();
        using var client = CreateClient(settings);
        await client.PutObjectAsync(new PutObjectRequest { BucketName = settings.BucketName, Key = storageKey, InputStream = content, ContentType = contentType, AutoCloseStream = false }, cancellationToken);
        return $"{settings.PublicBaseUrl.TrimEnd('/')}/{string.Join('/', storageKey.Split('/').Select(Uri.EscapeDataString))}";
    }

    public async Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var settings = RequireSettings(); using var client = CreateClient(settings);
        await client.DeleteObjectAsync(new DeleteObjectRequest { BucketName = settings.BucketName, Key = storageKey }, cancellationToken);
    }

    private R2Settings RequireSettings()
    {
        var s = options.Value;
        if (string.IsNullOrWhiteSpace(s.AccountId) || string.IsNullOrWhiteSpace(s.AccessKeyId) || string.IsNullOrWhiteSpace(s.SecretAccessKey) || string.IsNullOrWhiteSpace(s.BucketName) || !Uri.TryCreate(s.PublicBaseUrl, UriKind.Absolute, out var publicUri) || publicUri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("R2 storage is not configured. AccountId, AccessKeyId, SecretAccessKey, BucketName, and an HTTP(S) PublicBaseUrl are required.");
        return s;
    }

    private static AmazonS3Client CreateClient(R2Settings s) => new(new BasicAWSCredentials(s.AccessKeyId, s.SecretAccessKey), new AmazonS3Config { ServiceURL = $"https://{s.AccountId}.r2.cloudflarestorage.com", ForcePathStyle = true, AuthenticationRegion = "auto" });
}
