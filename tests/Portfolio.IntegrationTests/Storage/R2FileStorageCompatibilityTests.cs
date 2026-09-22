using Amazon.Runtime;
using Portfolio.Application.Common.Abstractions.Storage;
using Portfolio.Infrastructure.Storage;

namespace Portfolio.IntegrationTests.Storage;

public sealed class R2FileStorageCompatibilityTests
{
    [Fact]
    public void Client_and_put_request_disable_streaming_checksum_trailers_without_disabling_payload_signing()
    {
        var settings = new R2Settings
        {
            AccountId = "test-account",
            AccessKeyId = "test-access-key",
            SecretAccessKey = "test-secret-key",
            BucketName = "private-test-bucket",
        };
        using var content = new MemoryStream([1, 2, 3]);

        var configuration = R2FileStorage.CreateClientConfiguration(settings);
        var request = R2FileStorage.CreatePutObjectRequest(settings, "private/object.png", content, "image/png");

        Assert.Equal(RequestChecksumCalculation.WHEN_REQUIRED, configuration.RequestChecksumCalculation);
        Assert.False(request.UseChunkEncoding);
        Assert.Null(request.DisablePayloadSigning);
        Assert.Null(request.DisableDefaultChecksumValidation);
        Assert.Equal(settings.BucketName, request.BucketName);
        Assert.Equal("private/object.png", request.Key);
        Assert.Same(content, request.InputStream);
        Assert.False(request.AutoCloseStream);
    }

    [Fact]
    public void Private_storage_targets_only_the_private_bucket_and_has_no_public_url_contract()
    {
        var settings = new R2Settings
        {
            AccountId = "test-account",
            AccessKeyId = "test-access-key",
            SecretAccessKey = "test-secret-key",
            BucketName = "public-media",
            PrivateBucketName = "private-job-agent",
            PublicBaseUrl = "https://media.example.test",
        };
        using var content = new MemoryStream([1, 2, 3]);

        var configuration = R2PrivateFileStorage.CreateClientConfiguration(settings);
        var request = R2PrivateFileStorage.CreatePutObjectRequest(
            settings, "canonical-cv/generated.pdf", content, "application/pdf");

        Assert.Equal(RequestChecksumCalculation.WHEN_REQUIRED, configuration.RequestChecksumCalculation);
        Assert.False(request.UseChunkEncoding);
        Assert.Null(request.DisablePayloadSigning);
        Assert.Equal("private-job-agent", request.BucketName);
        Assert.NotEqual(settings.BucketName, request.BucketName);
        Assert.Equal("canonical-cv/generated.pdf", request.Key);
        Assert.DoesNotContain(
            typeof(IPrivateFileStorage).GetMethods(),
            method => method.Name.Contains("Url", StringComparison.OrdinalIgnoreCase));
    }
}
