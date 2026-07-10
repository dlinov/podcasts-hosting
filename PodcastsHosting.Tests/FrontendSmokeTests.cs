namespace PodcastsHosting.Tests;

using System.Net;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PodcastsHosting.Configuration;
using PodcastsHosting.Data;
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

    [Fact]
    public void PodcastOptions_AreBoundFromConfiguration()
    {
        var options = _factory.Services.GetRequiredService<IOptions<PodcastOptions>>().Value;

        Assert.Equal("Audiobooks", options.ChannelTitle);
        Assert.Equal("Audiobooks channel", options.ChannelDescription);
        Assert.Equal(new Uri("https://podcast-hosting-dffbg7bsc4hvgbax.polandcentral-01.azurewebsites.net/"), options.PublicBaseUrl);
        Assert.False(options.RegistrationOpen);
    }

    [Fact]
    public void PodcastOptions_WithRelativePublicBaseUrl_FailOnStartup()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["App:PublicBaseUrl"] = "/relative"
                })));

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains("App:PublicBaseUrl", exception.Message);
    }

    [Fact]
    public async Task Login_WhenRegistrationIsClosed_DoesNotRenderRegisterLink()
    {
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/Identity/Account/Login");

        Assert.DoesNotContain("Register as a new user", html);
    }

    [Fact]
    public async Task Register_WhenRegistrationIsClosed_RedirectsHome()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/Identity/Account/Register");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Layout_DoesNotRenderMissingPrivacyLink()
    {
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.DoesNotContain("Home/Privacy", html);
        Assert.DoesNotContain(">Privacy<", html);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/live")]
    public async Task LivenessHealthCheck_ReturnsHealthy(string path)
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(path);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", content);
    }

    [Fact]
    public void SqlServer_UsesRetryingExecutionStrategy()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.True(dbContext.Database.CreateExecutionStrategy().RetriesOnFailure);
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
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:ConnectionString"] = "UseDevelopmentStorage=true"
                }));

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IFileService>();
                services.AddScoped<IFileService, EmptyFileService>();
            });
        }
    }

    [Fact]
    public void BlobServiceClient_IsReusedAsSingleton()
    {
        var first = _factory.Services.GetRequiredService<BlobServiceClient>();
        var second = _factory.Services.GetRequiredService<BlobServiceClient>();

        Assert.Same(first, second);
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