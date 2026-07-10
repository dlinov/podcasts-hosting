namespace PodcastsHosting.Tests;

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PodcastsHosting.Models;
using PodcastsHosting.Services;

public class LargeUploadStressTests : IClassFixture<LargeUploadStressTests.LargeUploadTestFactory>
{
    private const long DefaultUploadSizeBytes = 220L * 1024 * 1024;
    private readonly LargeUploadTestFactory _factory;

    public LargeUploadStressTests(LargeUploadTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Upload_TypedRequestBindsMultipartFields()
    {
        const long uploadSizeBytes = 1024;
        var probe = _factory.Services.GetRequiredService<LargeUploadProbe>();
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        using var multipartContent = CreateMultipartContent(uploadSizeBytes);

        using var response = await client.PostAsync("/Home/Upload", multipartContent);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(uploadSizeBytes, probe.UploadedBytes);
    }

    [Fact]
    [Trait("Category", "Stress")]
    public async Task Upload_LargeFile_WhenStressTestEnabled_CompletesWithoutOutOfMemory()
    {
        if (!IsStressTestEnabled())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOTNET_GCHeapHardLimit"))
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOTNET_GCHeapHardLimitPercent")))
        {
            throw new InvalidOperationException(
                "Set DOTNET_GCHeapHardLimit or DOTNET_GCHeapHardLimitPercent before enabling the large upload stress test.");
        }

        var uploadSizeBytes = GetUploadSizeBytes();
        var probe = _factory.Services.GetRequiredService<LargeUploadProbe>();
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.Timeout = TimeSpan.FromMinutes(5);

        using var multipartContent = CreateMultipartContent(uploadSizeBytes);
        using var response = await client.PostAsync("/Home/Upload", multipartContent);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(uploadSizeBytes, probe.UploadedBytes);
    }

    private static bool IsStressTestEnabled()
    {
        return string.Equals(Environment.GetEnvironmentVariable("RUN_LARGE_UPLOAD_TESTS"), "true", StringComparison.OrdinalIgnoreCase);
    }

    private static long GetUploadSizeBytes()
    {
        var sizeMbValue = Environment.GetEnvironmentVariable("LARGE_UPLOAD_SIZE_MB");
        return int.TryParse(sizeMbValue, out var sizeMb) && sizeMb > 0
            ? sizeMb * 1024L * 1024L
            : DefaultUploadSizeBytes;
    }

    private static MultipartFormDataContent CreateMultipartContent(long uploadSizeBytes)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new StreamContent(new RepeatingByteStream(uploadSizeBytes), bufferSize: 64 * 1024);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");

        content.Add(fileContent, "Upload.File", "large-upload-test.mp3");
        content.Add(new StringContent("Large Upload Test"), "Upload.BookName");
        content.Add(new StringContent(string.Empty), "Upload.BookSeries");
        content.Add(new StringContent(string.Empty), "Upload.ChapterTitle");

        return content;
    }

    public sealed class LargeUploadTestFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<LargeUploadProbe>();
                services.RemoveAll<IFileService>();
                services.AddScoped<IFileService, StreamingFileService>();

                services.RemoveAll<UserManager<IdentityUser>>();
                services.AddScoped<UserManager<IdentityUser>, TestUserManager>();

                services.AddAuthentication(TestAuthenticationHandler.AuthenticationScheme)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.AuthenticationScheme,
                        _ => { });
            });
        }
    }

    private sealed class LargeUploadProbe
    {
        public long UploadedBytes { get; private set; }

        public void SetUploadedBytes(long uploadedBytes)
        {
            UploadedBytes = uploadedBytes;
        }
    }

    private sealed class StreamingFileService : IFileService
    {
        private readonly LargeUploadProbe _probe;

        public StreamingFileService(LargeUploadProbe probe)
        {
            _probe = probe;
        }

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

        public async Task UploadAudioAsync(
            IdentityUser user,
            IFormFile file,
            string bookName,
            string? bookSeries,
            string? chapterTitle,
            int? chapterNumber)
        {
            await using var stream = file.OpenReadStream();
            var buffer = new byte[64 * 1024];
            long uploadedBytes = 0;

            while (true)
            {
                var bytesRead = await stream.ReadAsync(buffer);
                if (bytesRead == 0)
                {
                    break;
                }

                uploadedBytes += bytesRead;
            }

            _probe.SetUploadedBytes(uploadedBytes);
        }
    }

    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string AuthenticationScheme = "LargeUploadTest";

        public TestAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, TestUserManager.TestUserId),
                new Claim(ClaimTypes.Name, TestUserManager.TestUserEmail),
                new Claim(ClaimTypes.Email, TestUserManager.TestUserEmail)
            };
            var identity = new ClaimsIdentity(claims, AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, AuthenticationScheme);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class TestUserManager : UserManager<IdentityUser>
    {
        public const string TestUserEmail = "large-upload-test@example.com";
        public const string TestUserId = "large-upload-test-user";

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

    private sealed class RepeatingByteStream : Stream
    {
        private static readonly byte[] Mp3Header = [(byte)'I', (byte)'D', (byte)'3', 4, 0, 0, 0, 0, 0, 0];
        private readonly long _length;
        private long _position;

        public RepeatingByteStream(long length)
        {
            _length = length;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadInto(buffer.AsSpan(offset, count));
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ReadInto(buffer.Span));
        }

        private int ReadInto(Span<byte> buffer)
        {
            if (_position >= _length)
            {
                return 0;
            }

            var bytesToRead = (int)Math.Min(buffer.Length, _length - _position);
            var startPosition = _position;

            for (var index = 0; index < bytesToRead; index++)
            {
                var absolutePosition = startPosition + index;
                buffer[index] = absolutePosition < Mp3Header.Length
                    ? Mp3Header[absolutePosition]
                    : (byte)42;
            }

            _position += bytesToRead;
            return bytesToRead;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}