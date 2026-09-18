using Amazon.Runtime;
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
}
