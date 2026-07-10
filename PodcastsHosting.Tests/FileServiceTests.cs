namespace PodcastsHosting.Tests;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PodcastsHosting.Data;
using PodcastsHosting.Models;
using PodcastsHosting.Services;

public class FileServiceTests
{
    [Fact]
    public async Task ListAllAudios_IncludesUploaderAfterTrackingIsCleared()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"file-service-{Guid.NewGuid()}")
            .Options;
        await using var dbContext = new ApplicationDbContext(options);
        var user = new IdentityUser
        {
            Id = Guid.NewGuid().ToString(),
            Email = "uploader@example.com",
            UserName = "uploader@example.com"
        };
        dbContext.Users.Add(user);
        dbContext.AudioModels.Add(new AudioModel
        {
            Id = Guid.NewGuid(),
            FileName = "Episode",
            FilePath = "https://storage.example/episode",
            FileHash = "hash",
            FileSize = 10,
            UploadTime = DateTime.UtcNow,
            Extension = ".mp3",
            UploadUserId = user.Id
        });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        var service = new FileService(NullLogger<FileService>.Instance, dbContext, new StubAudioBlobStorage());

        var audio = Assert.Single(await service.ListAllAudios());

        Assert.Equal(user.Email, audio.UploadUser?.Email);
    }

    [Fact]
    public async Task UploadAudioAsync_PersistsBlobResultAndMetadata()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"file-service-{Guid.NewGuid()}")
            .Options;
        await using var dbContext = new ApplicationDbContext(options);
        var user = new IdentityUser
        {
            Id = Guid.NewGuid().ToString(),
            Email = "uploader@example.com",
            UserName = "uploader@example.com"
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        var blobStorage = new RecordingAudioBlobStorage();
        var service = new FileService(NullLogger<FileService>.Instance, dbContext, blobStorage);
        var content = new byte[] { (byte)'I', (byte)'D', (byte)'3', 4 };
        var file = new FormFile(new MemoryStream(content), 0, content.Length, "file", "episode.mp3")
        {
            Headers = new HeaderDictionary(),
            ContentType = "audio/mpeg"
        };

        await service.UploadAudioAsync(user, file, "Book", "Series", "Chapter", 1);

        var audio = Assert.Single(await dbContext.AudioModels.AsNoTracking().ToListAsync());
        Assert.Equal(blobStorage.UploadedAudioId, audio.Id);
        Assert.Equal($"https://storage.example/{audio.Id}", audio.FilePath);
        Assert.Equal(Convert.ToBase64String(blobStorage.ContentHash), audio.FileHash);
        Assert.Equal(".mp3", audio.Extension);
        Assert.Equal(content.Length, audio.FileSize);
        Assert.Equal("audio/mpeg", blobStorage.UploadedContentType);
    }

    [Fact]
    public async Task DeleteAudioAsync_DeletesBlobAndMetadata()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"file-service-{Guid.NewGuid()}")
            .Options;
        await using var dbContext = new ApplicationDbContext(options);
        var audioId = Guid.NewGuid();
        dbContext.AudioModels.Add(new AudioModel
        {
            Id = audioId,
            FileName = "Episode",
            FilePath = $"https://storage.example/{audioId}",
            FileHash = "hash",
            FileSize = 10,
            UploadTime = DateTime.UtcNow,
            Extension = ".mp3"
        });
        await dbContext.SaveChangesAsync();
        var blobStorage = new RecordingAudioBlobStorage();
        var service = new FileService(NullLogger<FileService>.Instance, dbContext, blobStorage);

        var deleted = await service.DeleteAudioAsync(audioId);

        Assert.True(deleted);
        Assert.Equal(audioId, blobStorage.DeletedAudioId);
        Assert.Empty(await dbContext.AudioModels.ToListAsync());
    }

    private sealed class RecordingAudioBlobStorage : IAudioBlobStorage
    {
        public byte[] ContentHash { get; } = [1, 2, 3, 4];

        public Guid? UploadedAudioId { get; private set; }

        public string? UploadedContentType { get; private set; }

        public Guid? DeletedAudioId { get; private set; }

        public Task<Stream> OpenReadAsync(Guid audioId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<BlobUploadResult> UploadAsync(
            Guid audioId,
            Stream content,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            UploadedAudioId = audioId;
            UploadedContentType = contentType;
            return Task.FromResult(new BlobUploadResult(
                new Uri($"https://storage.example/{audioId}"),
                ContentHash));
        }

        public Task<bool> DeleteIfExistsAsync(Guid audioId, CancellationToken cancellationToken = default)
        {
            DeletedAudioId = audioId;
            return Task.FromResult(true);
        }
    }

    private sealed class StubAudioBlobStorage : IAudioBlobStorage
    {
        public Task<Stream> OpenReadAsync(Guid audioId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<BlobUploadResult> UploadAsync(
            Guid audioId,
            Stream content,
            string contentType,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> DeleteIfExistsAsync(Guid audioId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}