using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using Portfolio.Application.Common.Abstractions.Storage;

namespace Portfolio.Infrastructure.Storage;

public sealed class R2PrivateFileStorage(IOptions<R2Settings> options) : IPrivateFileStorage
{
    public async Task UploadAsync(string storageKey, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var settings = RequireSettings();
        using var client = CreateClient(settings);
        await client.PutObjectAsync(CreatePutObjectRequest(settings, storageKey, content, contentType), cancellationToken);
    }

    public async Task<Stream> OpenReadAsync(string storageKey, long maximumBytes, CancellationToken cancellationToken = default)
    {
        if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        var settings = RequireSettings();
        using var client = CreateClient(settings);
        using var response = await client.GetObjectAsync(settings.PrivateBucketName, storageKey, cancellationToken);
        if (response.ContentLength > maximumBytes) throw new InvalidOperationException("The stored object exceeds the permitted read size.");
        var content = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await response.ResponseStream.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (content.Length + read > maximumBytes) throw new InvalidOperationException("The stored object exceeds the permitted read size.");
            await content.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        content.Position = 0;
        return content;
    }

    public async Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var settings = RequireSettings();
        using var client = CreateClient(settings);
        await client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = settings.PrivateBucketName,
            Key = storageKey,
        }, cancellationToken);
    }

    private R2Settings RequireSettings()
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.AccountId)
            || string.IsNullOrWhiteSpace(settings.AccessKeyId)
            || string.IsNullOrWhiteSpace(settings.SecretAccessKey)
            || string.IsNullOrWhiteSpace(settings.PrivateBucketName))
        {
            throw new InvalidOperationException(
                "Private R2 storage is not configured. AccountId, AccessKeyId, SecretAccessKey, and PrivateBucketName are required.");
        }
        return settings;
    }

    internal static PutObjectRequest CreatePutObjectRequest(
        R2Settings settings, string storageKey, Stream content, string contentType) => new()
    {
        BucketName = settings.PrivateBucketName,
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
