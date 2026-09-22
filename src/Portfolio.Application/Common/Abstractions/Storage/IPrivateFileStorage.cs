namespace Portfolio.Application.Common.Abstractions.Storage;

public interface IPrivateFileStorage
{
    Task UploadAsync(string storageKey, Stream content, string contentType, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(string storageKey, long maximumBytes, CancellationToken cancellationToken = default);
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
}
