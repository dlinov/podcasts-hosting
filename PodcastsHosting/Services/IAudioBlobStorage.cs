namespace PodcastsHosting.Services;

public sealed record BlobUploadResult(Uri Uri, byte[]? ContentHash);

public interface IAudioBlobStorage
{
    Task<Stream> OpenReadAsync(Guid audioId, CancellationToken cancellationToken = default);

    Task<BlobUploadResult> UploadAsync(
        Guid audioId,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteIfExistsAsync(Guid audioId, CancellationToken cancellationToken = default);
}