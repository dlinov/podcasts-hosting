namespace PodcastsHosting.Tests;

using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using PodcastsHosting.Controllers;
using PodcastsHosting.Models;
using PodcastsHosting.Services;

public class HomeControllerSecurityTests
{
    private const long MaxUploadSizeBytes = 512L * 1024 * 1024;

    [Fact]
    public void Upload_PostRequestSizeLimit_Is512MiB()
    {
        var method = typeof(HomeController).GetMethod(
            nameof(HomeController.Upload),
            [typeof(IFormFile), typeof(string), typeof(string), typeof(string), typeof(int?)]);

        Assert.NotNull(method);
        var requestSizeLimit = Assert.Single(
            method.GetCustomAttributesData(),
            attribute => attribute.AttributeType == typeof(RequestSizeLimitAttribute));
        var bytesArgument = Assert.Single(requestSizeLimit.ConstructorArguments);
        Assert.Equal(MaxUploadSizeBytes, bytesArgument.Value);
    }

    [Fact]
    public void Delete_IsPostOnlyAndRequiresAntiforgery()
    {
        var method = typeof(HomeController).GetMethod(nameof(HomeController.Delete), [typeof(Guid)]);

        Assert.NotNull(method);
        Assert.NotNull(method.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(method.GetCustomAttribute<AuthorizeAttribute>());
        Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.Null(method.GetCustomAttribute<HttpGetAttribute>());
    }

    [Fact]
    public async Task Delete_WhenAudioExists_RemovesAudioAndRedirectsToUpload()
    {
        var audioId = Guid.NewGuid();
        var fileService = new DeletingFileService(CreateAudio(audioId));
        var controller = CreateController(fileService);

        var result = await controller.Delete(audioId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(HomeController.Upload), redirect.ActionName);
        Assert.Equal(audioId, fileService.DeletedAudioId);
        Assert.Empty(await fileService.ListAllAudios());
    }

    [Fact]
    public async Task Delete_WhenAudioDoesNotExist_ReturnsNotFoundWithoutDeleting()
    {
        var fileService = new DeletingFileService();
        var controller = CreateController(fileService);

        var result = await controller.Delete(Guid.NewGuid());

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Null(fileService.DeletedAudioId);
    }

    private static HomeController CreateController(IFileService fileService)
    {
        return new HomeController(
            NullLogger<HomeController>.Instance,
            configuration: null!,
            userManager: null!,
            fileService);
    }

    private static AudioModel CreateAudio(Guid audioId)
    {
        return new AudioModel
        {
            Id = audioId,
            FileName = "Episode",
            FilePath = $"https://storage.example/{audioId}",
            FileHash = "hash",
            FileSize = 10,
            UploadTime = DateTime.UtcNow,
            Extension = ".mp3"
        };
    }

    private sealed class DeletingFileService : IFileService
    {
        private readonly Dictionary<Guid, AudioModel> _audioModels;

        public DeletingFileService(params AudioModel[] audioModels)
        {
            _audioModels = audioModels.ToDictionary(audio => audio.Id);
        }

        public Guid? DeletedAudioId { get; private set; }

        public Task<List<AudioModel>> ListAllAudios()
        {
            return Task.FromResult(_audioModels.Values.ToList());
        }

        public ValueTask<AudioModel?> GetAudioAsync(Guid audioId)
        {
            _audioModels.TryGetValue(audioId, out var audioModel);
            return ValueTask.FromResult(audioModel);
        }

        public Task<Stream> OpenAudioReadStreamAsync(Guid audioId)
        {
            throw new NotSupportedException();
        }

        public Task<bool> DeleteAudioAsync(Guid audioId)
        {
            DeletedAudioId = audioId;
            return Task.FromResult(_audioModels.Remove(audioId));
        }

        public Task UploadAudioAsync(
            IdentityUser user,
            IFormFile file,
            string bookName,
            string? bookSeries,
            string? chapterTitle,
            int? chapterNumber)
        {
            throw new NotSupportedException();
        }
    }
}