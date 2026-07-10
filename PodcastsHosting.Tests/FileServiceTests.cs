namespace PodcastsHosting.Tests;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:ConnectionString"] = "UseDevelopmentStorage=true"
            })
            .Build();
        var service = new FileService(NullLogger<FileService>.Instance, configuration, dbContext);

        var audio = Assert.Single(await service.ListAllAudios());

        Assert.Equal(user.Email, audio.UploadUser?.Email);
    }
}