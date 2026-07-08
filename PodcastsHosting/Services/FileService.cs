using System.Security.Cryptography;

namespace PodcastsHosting.Services;

using System.Data.Common;
using System.Text;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PodcastsHosting.Data;
using PodcastsHosting.Models;

public class FileService : IFileService
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
                            throw new InvalidOperationException("No connection string found");
    }

    public Task<List<AudioModel>> ListAllAudios()
    {
        return _dbContext.AudioModels.OrderBy(x => x.UploadTime).ToListAsync();
    }

    public ValueTask<AudioModel?> GetAudioAsync(Guid audioId)
    {
        return _dbContext.AudioModels.FindAsync(audioId);
    }

    public async Task<Stream> OpenAudioReadStreamAsync(Guid audioId)
    {
        var blobClient = await BuildBlobClientAsync(audioId);
        return await blobClient.OpenReadAsync(new BlobOpenReadOptions(allowModifications: false));
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
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
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
            _logger.LogWarning("[audio-{AudioId}] Uploaded blob hash is null, trying to calculate it (CanSeek={CanSeek})",
                audioId, stream.CanSeek);
            stream.Seek(0, SeekOrigin.Begin);
            valueContentHash = await MD5.Create().ComputeHashAsync(stream);
            _logger.LogInformation("[audio-{AudioId}] Hash of the file was calculated", audioId);
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
        await containerClient.CreateIfNotExistsAsync();
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

public static class AudioFileValidator
{
    private const int HeaderBytesToRead = 512;
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3",
        ".m4a",
        ".m4b"
    };

    private static readonly HashSet<string> SupportedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/mp4",
        "application/octet-stream",
        "audio/m4a",
        "audio/m4b",
        "audio/mp3",
        "audio/mp4",
        "audio/mpeg",
        "audio/x-m4a",
        "audio/x-m4b",
        "audio/x-mp3",
        "audio/x-mpeg"
    };

    public static async Task<string?> GetValidationErrorAsync(IFormFile file)
    {
        var extension = Path.GetExtension(file.FileName);
        if (!SupportedExtensions.Contains(extension))
        {
            return "Only MP3, M4A, and M4B audio files are supported.";
        }

        if (!string.IsNullOrWhiteSpace(file.ContentType) && !SupportedContentTypes.Contains(file.ContentType))
        {
            return "The uploaded file content type is not supported.";
        }

        var header = new byte[HeaderBytesToRead];
        await using var stream = file.OpenReadStream();
        var bytesRead = await stream.ReadAsync(header);
        var headerBytes = header.AsSpan(0, bytesRead);

        return HasAudioSignature(extension, headerBytes)
            ? null
            : "The uploaded file does not look like a supported audio file.";
    }

    private static bool HasAudioSignature(string extension, ReadOnlySpan<byte> header)
    {
        return extension.ToLowerInvariant() switch
        {
            ".mp3" => HasMp3Signature(header),
            ".m4a" => HasIsoBaseMediaSignature(header, "M4A"),
            ".m4b" => HasIsoBaseMediaSignature(header, "M4B"),
            _ => false
        };
    }

    private static bool HasMp3Signature(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 'I' && header[1] == 'D' && header[2] == '3')
        {
            return true;
        }

        return header.Length >= 2 && header[0] == 0xff && (header[1] & 0xe0) == 0xe0;
    }

    private static bool HasIsoBaseMediaSignature(ReadOnlySpan<byte> header, string expectedBrand)
    {
        if (header.Length < 12 || header[4] != 'f' || header[5] != 't' || header[6] != 'y' || header[7] != 'p')
        {
            return false;
        }

        var headerText = Encoding.ASCII.GetString(header);
        return headerText.Contains(expectedBrand, StringComparison.Ordinal);
    }
}