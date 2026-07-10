namespace PodcastsHosting.Services;

using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

public sealed class AudioBlobStorage : IAudioBlobStorage
{
    private const string ContainerName = "audiofiles";
    private const int TransferChunkSize = 4 * 1024 * 1024;
    private readonly BlobContainerClient _containerClient;
    private readonly SemaphoreSlim _containerInitializationLock = new(1, 1);
    private volatile bool _containerInitialized;

    public AudioBlobStorage(BlobServiceClient blobServiceClient)
    {
        _containerClient = blobServiceClient.GetBlobContainerClient(ContainerName);
    }

    public async Task<Stream> OpenReadAsync(Guid audioId, CancellationToken cancellationToken = default)
    {
        await EnsureContainerExistsAsync(cancellationToken);
        return await GetBlobClient(audioId).OpenReadAsync(
            new BlobOpenReadOptions(allowModifications: false),
            cancellationToken);
    }

    public async Task<BlobUploadResult> UploadAsync(
        Guid audioId,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        await EnsureContainerExistsAsync(cancellationToken);
        var blobClient = GetBlobClient(audioId);
        var response = await blobClient.UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = contentType
            },
            TransferOptions = new StorageTransferOptions
            {
                InitialTransferSize = TransferChunkSize,
                MaximumTransferSize = TransferChunkSize,
                MaximumConcurrency = 1
            }
        }, cancellationToken);

        return new BlobUploadResult(blobClient.Uri, response.Value.ContentHash);
    }

    public async Task<bool> DeleteIfExistsAsync(Guid audioId, CancellationToken cancellationToken = default)
    {
        await EnsureContainerExistsAsync(cancellationToken);
        return (await GetBlobClient(audioId).DeleteIfExistsAsync(cancellationToken: cancellationToken)).Value;
    }

    private BlobClient GetBlobClient(Guid audioId)
    {
        return _containerClient.GetBlobClient(audioId.ToString());
    }

    private async Task EnsureContainerExistsAsync(CancellationToken cancellationToken)
    {
        if (_containerInitialized)
        {
            return;
        }

        await _containerInitializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_containerInitialized)
            {
                return;
            }

            await _containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            _containerInitialized = true;
        }
        finally
        {
            _containerInitializationLock.Release();
        }
    }
}