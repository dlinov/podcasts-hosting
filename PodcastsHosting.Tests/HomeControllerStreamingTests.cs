namespace PodcastsHosting.Tests;

using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Net.Http.Headers;
using PodcastsHosting.Controllers;
using PodcastsHosting.Models;
using PodcastsHosting.Services;

public class HomeControllerStreamingTests
{
    [Fact]
    public async Task Download_ForPodcastPlayback_ReturnsRangeEnabledStreamWithoutAttachment()
    {
        var audioId = Guid.NewGuid();
        var audio = CreateAudio(audioId, ".MP3");
        var controller = CreateController(new FakeFileService([audio], Encoding.ASCII.GetBytes("0123456789")));

        var result = await controller.Download(audioId);

        var fileResult = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("audio/mpeg", fileResult.ContentType);
        Assert.True(fileResult.EnableRangeProcessing);
        Assert.True(string.IsNullOrEmpty(fileResult.FileDownloadName));
        Assert.True(fileResult.FileStream.CanSeek);
    }

    [Fact]
    public async Task Download_WithRangeHeader_WritesPartialContent()
    {
        var audioId = Guid.NewGuid();
        var audio = CreateAudio(audioId, ".mp3");
        var controller = CreateController(new FakeFileService([audio], Encoding.ASCII.GetBytes("0123456789")));
        var result = Assert.IsType<FileStreamResult>(await controller.Download(audioId));
        await using var responseBody = new MemoryStream();
        var actionContext = CreateActionContext(responseBody, "bytes=2-5");

        await result.ExecuteResultAsync(actionContext);

        Assert.Equal(StatusCodes.Status206PartialContent, actionContext.HttpContext.Response.StatusCode);
        Assert.Equal("bytes 2-5/10", actionContext.HttpContext.Response.Headers[HeaderNames.ContentRange].ToString());
        Assert.Equal(4, actionContext.HttpContext.Response.ContentLength);
        Assert.Equal("2345", Encoding.ASCII.GetString(responseBody.ToArray()));
    }

    [Fact]
    public async Task Download_WithDownloadFlag_ReturnsAttachmentFileName()
    {
        var audioId = Guid.NewGuid();
        var audio = CreateAudio(audioId, ".m4b");
        var controller = CreateController(new FakeFileService([audio], Encoding.ASCII.GetBytes("0123456789")));

        var result = await controller.Download(audioId, download: true);

        var fileResult = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("audio/mp4", fileResult.ContentType);
        Assert.Equal($"{audioId}.m4b", fileResult.FileDownloadName);
        Assert.True(fileResult.EnableRangeProcessing);
    }

    [Fact]
    public async Task Rss_UsesStreamableAudioUrlAndAppleCompatibleMimeType()
    {
        var audioId = Guid.NewGuid();
        var audio = CreateAudio(audioId, ".m4b");
        var controller = CreateController(new FakeFileService([audio], Encoding.ASCII.GetBytes("0123456789")));

        var result = await controller.Rss();

        var contentResult = Assert.IsType<ContentResult>(result);
        var document = XDocument.Parse(contentResult.Content!);
        var enclosure = Assert.Single(document.Descendants("enclosure"));
        Assert.Equal($"https://podcasts.example/audio/{audioId}.m4b", enclosure.Attribute("url")?.Value);
        Assert.Equal("audio/mp4", enclosure.Attribute("type")?.Value);
        Assert.Equal(audio.FileSize.ToString(), enclosure.Attribute("length")?.Value);
    }

    private static HomeController CreateController(IFileService fileService)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:ChannelTitle"] = "Test Podcast",
                ["App:ChannelDescription"] = "Test feed",
                ["App:PublicBaseUrl"] = "https://podcasts.example/"
            })
            .Build();

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString("spoofed.example");

        return new HomeController(
            NullLogger<HomeController>.Instance,
            configuration,
            userManager: null!,
            fileService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private static ActionContext CreateActionContext(Stream responseBody, string rangeHeader)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddMvcCore()
            .Services
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            Response =
            {
                Body = responseBody
            }
        };
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.Headers[HeaderNames.Range] = rangeHeader;

        return new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
    }

    private static AudioModel CreateAudio(Guid audioId, string extension)
    {
        return new AudioModel
        {
            Id = audioId,
            FileName = "Episode",
            FilePath = $"https://storage.example/{audioId}",
            FileHash = "hash",
            FileSize = 10,
            UploadTime = DateTimeOffset.Parse("2026-07-08T12:00:00Z").UtcDateTime,
            Extension = extension
        };
    }

    private sealed class FakeFileService : IFileService
    {
        private readonly List<AudioModel> _audioModels;
        private readonly byte[] _content;

        public FakeFileService(List<AudioModel> audioModels, byte[] content)
        {
            _audioModels = audioModels;
            _content = content;
        }

        public Task<List<AudioModel>> ListAllAudios()
        {
            return Task.FromResult(_audioModels.OrderBy(audio => audio.UploadTime).ToList());
        }

        public ValueTask<AudioModel?> GetAudioAsync(Guid audioId)
        {
            return ValueTask.FromResult(_audioModels.SingleOrDefault(audio => audio.Id == audioId));
        }

        public Task<Stream> OpenAudioReadStreamAsync(Guid audioId)
        {
            return Task.FromResult<Stream>(new MemoryStream(_content, writable: false));
        }

        public Task<bool> DeleteAudioAsync(Guid audioId)
        {
            throw new NotSupportedException();
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