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

    public async Task UploadPrivateAsync(string storageKey, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var settings = RequireSettings();
        await PutAsync(settings, storageKey, content, contentType, cancellationToken);
    }

    public async Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var settings = RequireSettings(); using var client = CreateClient(settings);
        await client.DeleteObjectAsync(new DeleteObjectRequest { BucketName = settings.BucketName, Key = storageKey }, cancellationToken);
    }

    public async Task<Stream> OpenReadAsync(string storageKey, long maximumBytes, CancellationToken cancellationToken = default)
    {
        if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        var settings = RequireSettings(); using var client = CreateClient(settings);
        using var response = await client.GetObjectAsync(settings.BucketName, storageKey, cancellationToken);
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
        await client.PutObjectAsync(new PutObjectRequest { BucketName = settings.BucketName, Key = storageKey, InputStream = content, ContentType = contentType, AutoCloseStream = false }, cancellationToken);
    }

    private static AmazonS3Client CreateClient(R2Settings s) => new(new BasicAWSCredentials(s.AccessKeyId, s.SecretAccessKey), new AmazonS3Config { ServiceURL = $"https://{s.AccountId}.r2.cloudflarestorage.com", ForcePathStyle = true, AuthenticationRegion = "auto" });
}
