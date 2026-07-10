using System.Security.Cryptography;

namespace PodcastsHosting.Services;

using System.Data.Common;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PodcastsHosting.Data;
using PodcastsHosting.Models;

public class FileService : IFileService
{
    private readonly ILogger<FileService> _logger;
    private readonly ApplicationDbContext _dbContext;
    private readonly IAudioBlobStorage _blobStorage;

    public FileService(
        ILogger<FileService> logger,
        ApplicationDbContext dbContext,
        IAudioBlobStorage blobStorage)
    {
        _logger = logger;
        _dbContext = dbContext;
        _blobStorage = blobStorage;
    }

    public Task<List<AudioModel>> ListAllAudios()
    {
        return _dbContext.AudioModels
            .AsNoTracking()
            .Include(audio => audio.UploadUser)
            .OrderBy(audio => audio.UploadTime)
            .ToListAsync();
    }

    public ValueTask<AudioModel?> GetAudioAsync(Guid audioId)
    {
        return _dbContext.AudioModels.FindAsync(audioId);
    }

    public async Task<Stream> OpenAudioReadStreamAsync(Guid audioId)
    {
        return await _blobStorage.OpenReadAsync(audioId);
    }

    public async Task<bool> DeleteAudioAsync(Guid audioId)
    {
        var audioModel = await GetAudioAsync(audioId);
        if (audioModel == null) return false;

        await _blobStorage.DeleteIfExistsAsync(audioId);

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

        await using var stream = file.OpenReadStream();
        var uploadResult = await _blobStorage.UploadAsync(audioId, stream, file.ContentType);

        _logger.LogInformation("[audio-{AudioId}] File {FileName} was uploaded successfully",
            audioId, file.FileName);
        var valueContentHash = uploadResult.ContentHash;
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
            FilePath = uploadResult.Uri.ToString(),
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
            await _blobStorage.DeleteIfExistsAsync(audioId);
            throw;
        }

        _logger.LogInformation("File {audioModel.FileName} uploaded successfully by {user.Email}", audioModel.FileName,
            user.Email);
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