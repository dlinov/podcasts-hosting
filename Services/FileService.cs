using System.Security.Cryptography;

namespace PodcastsHosting.Services;

using System.Configuration;
using System.Data.Common;
using System.Text;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PodcastsHosting.Data;
using PodcastsHosting.Models;

public class FileService
{
    private const string AccountName = "podcasthostingstorage";
    private const string ContainerName = "audiofiles";
    private readonly ILogger<FileService> _logger;
    private readonly ApplicationDbContext _dbContext;
    private readonly string _connectionString;

    public FileService(
        ILogger<FileService> logger,
        IConfiguration configuration,
        ApplicationDbContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;
        _connectionString = configuration["Storage:ConnectionString"] ??
                            throw new ConfigurationErrorsException("No connection string found");
    }

    public Task<List<AudioModel>> ListAllAudios()
    {
        return _dbContext.AudioModels.OrderBy(x => x.UploadTime).ToListAsync();
    }

    public ValueTask<AudioModel?> GetAudioAsync(Guid audioId)
    {
        return _dbContext.AudioModels.FindAsync(audioId);
    }

    public async Task<Stream> DownloadAudioAsync(Guid audioId)
    {
        var blobClient = await BuildBlobClientAsync(audioId);
        var blobDownloadInfo = await blobClient.DownloadAsync();
        return blobDownloadInfo.Value.Content;
    }

    public async Task<bool> DeleteAudioAsync(Guid audioId)
    {
        var audioModel = await GetAudioAsync(audioId);
        if (audioModel == null) return false;

        var blobClient = await BuildBlobClientAsync(audioId);
        await blobClient.DeleteIfExistsAsync();

        _dbContext.AudioModels.Remove(audioModel);
        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task UploadAudioAsync(IdentityUser user, IFormFile file, string bookName, string? bookSeries,
        string? chapterTitle, int? chapterNumber)
    {
        var audioId = Guid.NewGuid();
        var extension = Path.GetExtension(file.FileName);
        var customTitle = BuildTitle(bookName, bookSeries, chapterTitle, chapterNumber);

        var blobClient = await BuildBlobClientAsync(audioId);
        await using var stream = file.OpenReadStream();
        var resp = await blobClient.UploadAsync(stream, true);
        if (!resp.HasValue)
        {
            using var rawResponse = resp.GetRawResponse();
            _logger.LogWarning("[audio-{AudioId}] File {FileName} could not be uploaded. Status: {Status}",
                audioId, file.FileName, rawResponse.Status);
            var errorMessage = $"[audio-{audioId}] Failed to upload file {file.FileName}. " +
                               $"Info: '{resp.Value}'. " +
                               $"Headers: [{string.Join("; ", rawResponse.Headers.Select(x => x.ToString()))}]. " +
                               $"Status: '{rawResponse.Status}'";
            throw new Exception(errorMessage);
        }

        _logger.LogInformation("[audio-{AudioId}] File {FileName} was uploaded successfully",
            audioId, file.FileName);
        var valueContentHash = resp.Value.ContentHash;
        if (valueContentHash == null)
        {
            _logger.LogWarning("[audio-{AudioId}] Uploaded blob hash is null, trying to calculate it (CanSeek={CanSeek}",
                audioId, stream.CanSeek);
            stream.Seek(0, SeekOrigin.Begin);
            valueContentHash = await MD5.Create().ComputeHashAsync(stream).ConfigureAwait(false);
            if (valueContentHash == null || valueContentHash.Length == 0)
            {
                _logger.LogWarning("[audio-{AudioId}] Failed to calculate hash of the file", audioId);
            }
            else
            {
                _logger.LogInformation("[audio-{AudioId}] Hash of the file was calculated", audioId);
            }
        }
        var blobHash = Convert.ToBase64String(valueContentHash);

        var audioModel = new AudioModel
        {
            Id = audioId,
            FileName = customTitle.ToString(),
            FilePath = blobClient.Uri.ToString(),
            FileSize = file.Length,
            FileHash = blobHash,
            Extension = extension,
            UploadTime = DateTime.UtcNow,
            UploadUser = user
        };

        _dbContext.AudioModels.Add(audioModel);
        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbException)
        {
            await blobClient.DeleteIfExistsAsync();
            throw;
        }

        _logger.LogInformation("File {audioModel.FileName} uploaded successfully by {user.Email}", audioModel.FileName,
            user.Email);
    }

    private async Task<BlobClient> BuildBlobClientAsync(Guid audioId)
    {
        var blobServiceClient = new BlobServiceClient(_connectionString);
        var containerClient = blobServiceClient.GetBlobContainerClient(ContainerName);
        await containerClient.CreateIfNotExistsAsync().ConfigureAwait(false);
        var blobClient = containerClient.GetBlobClient(audioId.ToString());
        return blobClient;
    }

    private static StringBuilder BuildTitle(
        string bookName,
        string? bookSeries,
        string? chapterTitle,
        int? chapterNumber)
    {
        var customTitle = new StringBuilder(bookName);

        if (!string.IsNullOrWhiteSpace(bookSeries))
        {
            customTitle.Append($" [{bookSeries}]");
        }

        if (!string.IsNullOrWhiteSpace(chapterTitle))
        {
            if (chapterNumber != null)
            {
                customTitle.Append($" | {chapterNumber} {chapterTitle}");
            }
            else
            {
                customTitle.Append($" | {chapterTitle}");
            }
        }

        return customTitle;
    }
}