namespace PodcastsHosting.Tests;

using System.Security.Claims;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PodcastsHosting.Controllers;
using PodcastsHosting.Models;
using PodcastsHosting.Services;

public class HomeControllerSecurityTests
{
    private const long MaxUploadSizeBytes = 1024L * 1024 * 1024;

    [Fact]
    public void Upload_PostRequestSizeLimit_Is1GiB()
    {
        var method = typeof(HomeController).GetMethod(
            nameof(HomeController.Upload),
            [typeof(UploadAudioRequest)]);

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

    [Fact]
    public async Task Upload_WhenFileIsNotAudio_ReturnsModelErrorWithoutUploading()
    {
        var fileService = new DeletingFileService();
        var controller = CreateController(fileService);
        var file = CreateFormFile([(byte)'<', (byte)'h', (byte)'t', (byte)'m', (byte)'l'], "episode.mp3", "audio/mpeg");

        var result = await controller.Upload(CreateUploadRequest(file));

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.False(fileService.UploadWasCalled);
    }

    [Fact]
    public async Task Upload_WhenUploadFails_DoesNotExposeRawExceptionMessage()
    {
        var fileService = new DeletingFileService
        {
            UploadException = new InvalidOperationException("secret storage connection string")
        };
        var controller = CreateController(fileService);
        var file = CreateFormFile([(byte)'I', (byte)'D', (byte)'3', 4], "episode.mp3", "audio/mpeg");

        var result = await controller.Upload(CreateUploadRequest(file));

        Assert.IsType<ViewResult>(result);
        var error = Assert.Single(controller.ModelState["Upload.File"]!.Errors);
        Assert.Equal("The file could not be uploaded. Please try again later.", error.ErrorMessage);
        Assert.DoesNotContain("secret", error.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static HomeController CreateController(IFileService fileService)
    {
        return new HomeController(
            NullLogger<HomeController>.Instance,
            configuration: null!,
            new TestUserManager(),
            fileService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, TestUserManager.TestUserId),
                        new Claim(ClaimTypes.Name, TestUserManager.TestUserEmail)
                    ],
                    authenticationType: "Test"))
                }
            }
        };
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

    private static IFormFile CreateFormFile(byte[] content, string fileName, string contentType)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    private static UploadAudioRequest CreateUploadRequest(IFormFile file)
    {
        return new UploadAudioRequest
        {
            File = file,
            BookName = "Book"
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

        public bool UploadWasCalled { get; private set; }

        public Exception? UploadException { get; init; }

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
            UploadWasCalled = true;
            if (UploadException != null)
            {
                throw UploadException;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class TestUserManager : UserManager<IdentityUser>
    {
        public const string TestUserEmail = "upload-test@example.com";
        public const string TestUserId = "upload-test-user";

        public TestUserManager()
            : base(
                new TestUserStore(),
                Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
                new PasswordHasher<IdentityUser>(),
                [],
                [],
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                new ServiceCollection().BuildServiceProvider(),
                NullLogger<UserManager<IdentityUser>>.Instance)
        {
        }

        public override Task<IdentityUser?> GetUserAsync(ClaimsPrincipal principal)
        {
            return Task.FromResult<IdentityUser?>(new IdentityUser
            {
                Id = TestUserId,
                Email = TestUserEmail,
                UserName = TestUserEmail
            });
        }
    }

    private sealed class TestUserStore : IUserStore<IdentityUser>
    {
        public void Dispose()
        {
        }

        public Task<string> GetUserIdAsync(IdentityUser user, CancellationToken cancellationToken)
        {
            return Task.FromResult(user.Id);
        }

        public Task<string?> GetUserNameAsync(IdentityUser user, CancellationToken cancellationToken)
        {
            return Task.FromResult(user.UserName);
        }

        public Task SetUserNameAsync(IdentityUser user, string? userName, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<string?> GetNormalizedUserNameAsync(IdentityUser user, CancellationToken cancellationToken)
        {
            return Task.FromResult(user.NormalizedUserName);
        }

        public Task SetNormalizedUserNameAsync(IdentityUser user, string? normalizedName, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IdentityResult> CreateAsync(IdentityUser user, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IdentityResult> UpdateAsync(IdentityUser user, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IdentityResult> DeleteAsync(IdentityUser user, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IdentityUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IdentityUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}