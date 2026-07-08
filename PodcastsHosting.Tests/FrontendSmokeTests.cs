namespace PodcastsHosting.Tests;

using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PodcastsHosting.Models;
using PodcastsHosting.Services;

public class FrontendSmokeTests : IClassFixture<FrontendSmokeTests.FrontendSmokeTestFactory>
{
    private readonly FrontendSmokeTestFactory _factory;

    public FrontendSmokeTests(FrontendSmokeTestFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Identity/Account/Login")]
    public async Task Page_ReturnsSuccess(string path)
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/css/site.css", "text/css")]
    [InlineData("/js/site.js", "text/javascript")]
    [InlineData("/favicon.ico", "image/x-icon")]
    [InlineData("/images/logo.jpg", "image/jpeg")]
    [InlineData("/lib/bootstrap/dist/css/bootstrap.min.css", "text/css")]
    [InlineData("/lib/bootstrap/dist/js/bootstrap.bundle.min.js", "text/javascript")]
    [InlineData("/lib/jquery/dist/jquery.min.js", "text/javascript")]
    [InlineData("/lib/jquery-validation/dist/jquery.validate.min.js", "text/javascript")]
    [InlineData("/lib/jquery-validation-unobtrusive/jquery.validate.unobtrusive.min.js", "text/javascript")]
    public async Task FrontendAsset_ReturnsSuccess(string path, string expectedMediaType)
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedMediaType, response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Content.Headers.ContentLength > 0);
    }

    public sealed class FrontendSmokeTestFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IFileService>();
                services.AddScoped<IFileService, EmptyFileService>();
            });
        }
    }

    private sealed class EmptyFileService : IFileService
    {
        public Task<List<AudioModel>> ListAllAudios()
        {
            return Task.FromResult(new List<AudioModel>());
        }

        public ValueTask<AudioModel?> GetAudioAsync(Guid audioId)
        {
            return ValueTask.FromResult<AudioModel?>(null);
        }

        public Task<Stream> OpenAudioReadStreamAsync(Guid audioId)
        {
            throw new NotSupportedException();
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