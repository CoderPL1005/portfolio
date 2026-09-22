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
        var settings = RequireSettings(requirePublicBaseUrl: true);
        await PutAsync(settings, storageKey, content, contentType, cancellationToken);
        return $"{settings.PublicBaseUrl.TrimEnd('/')}/{string.Join('/', storageKey.Split('/').Select(Uri.EscapeDataString))}";
    }

    public async Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var settings = RequireSettings(); using var client = CreateClient(settings);
        await client.DeleteObjectAsync(new DeleteObjectRequest { BucketName = settings.BucketName, Key = storageKey }, cancellationToken);
    }

    private R2Settings RequireSettings(bool requirePublicBaseUrl = false)
    {
        var s = options.Value;
        var publicUrlValid = Uri.TryCreate(s.PublicBaseUrl, UriKind.Absolute, out var publicUri) && publicUri.Scheme is "http" or "https";
        if (string.IsNullOrWhiteSpace(s.AccountId) || string.IsNullOrWhiteSpace(s.AccessKeyId) || string.IsNullOrWhiteSpace(s.SecretAccessKey) || string.IsNullOrWhiteSpace(s.BucketName) || requirePublicBaseUrl && !publicUrlValid)
            throw new InvalidOperationException("R2 storage is not configured. AccountId, AccessKeyId, SecretAccessKey, and BucketName are required; public uploads also require an HTTP(S) PublicBaseUrl.");
        return s;
    }

    private static async Task PutAsync(R2Settings settings, string storageKey, Stream content, string contentType, CancellationToken cancellationToken)
    {
        using var client = CreateClient(settings);
        await client.PutObjectAsync(CreatePutObjectRequest(settings, storageKey, content, contentType), cancellationToken);
    }

    internal static PutObjectRequest CreatePutObjectRequest(R2Settings settings, string storageKey, Stream content, string contentType) => new()
    {
        BucketName = settings.BucketName,
        Key = storageKey,
        InputStream = content,
        ContentType = contentType,
        AutoCloseStream = false,
        UseChunkEncoding = false,
    };

    internal static AmazonS3Config CreateClientConfiguration(R2Settings settings) => new()
    {
        ServiceURL = $"https://{settings.AccountId}.r2.cloudflarestorage.com",
        ForcePathStyle = true,
        AuthenticationRegion = "auto",
        RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
    };

    private static AmazonS3Client CreateClient(R2Settings settings) =>
        new(new BasicAWSCredentials(settings.AccessKeyId, settings.SecretAccessKey), CreateClientConfiguration(settings));
}
