namespace PodcastsHosting.EndToEndTests;

using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Xml.Linq;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Azure.Storage.Blobs;
using Xunit.Abstractions;
using Xunit.Sdk;

public class PodcastWorkflowEndToEndTests
{
    private readonly ITestOutputHelper _output;

    public PodcastWorkflowEndToEndTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "DockerE2E")]
    public async Task CompletePodcastWorkflow_UsesCleanDatabaseAndStorage()
    {
        if (!IsEnabled())
        {
            return;
        }

        await using var environment = await DockerComposeEnvironment.StartAsync(_output);
        try
        {
            var uploadSpecs = CreateUploadSpecs();
            var email = $"e2e-{Guid.NewGuid():N}@example.com";
            var password = $"E2e!{Guid.NewGuid():N}aA1";

            await AssertInitialStateIsCleanAsync(environment);
            await AssertUploadRequiresAuthenticationAsync(environment);
            await RegisterAsync(environment, email, password);

            using var authenticatedClient = environment.CreateHttpClient();
            await LoginAsync(authenticatedClient, email, password);
            foreach (var uploadSpec in uploadSpecs)
            {
                await UploadAsync(authenticatedClient, uploadSpec);
            }

            await AssertUploadPageAsync(authenticatedClient, uploadSpecs, email);
            var mediaReferences = await AssertFeedAsync(environment, uploadSpecs);
            await AssertBlobStorageAsync(environment, mediaReferences);
            await AssertMediaResponsesAsync(environment, mediaReferences);
        }
        catch
        {
            await environment.WriteDiagnosticsAsync();
            throw;
        }
    }

    private static bool IsEnabled()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("RUN_DOCKER_E2E_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<UploadSpec> CreateUploadSpecs()
    {
        var sizes = ParseUploadSizes();
        return
        [
            new UploadSpec(
                $"E2E Small {sizes[0]} MiB",
                "e2e-small.mp3",
                "audio/mpeg",
                sizes[0] * 1024L * 1024L,
                [(byte)'I', (byte)'D', (byte)'3', 4, 0, 0, 0, 0, 0, 0],
                0x2a),
            new UploadSpec(
                $"E2E Medium {sizes[1]} MiB",
                "e2e-medium.aac",
                "audio/aac",
                sizes[1] * 1024L * 1024L,
                [0xff, 0xf1, 0x50, 0x80],
                0x5a),
            new UploadSpec(
                $"E2E Large {sizes[2]} MiB",
                "e2e-large.m4a",
                "audio/mp4",
                sizes[2] * 1024L * 1024L,
                [
                    0, 0, 0, 20,
                    (byte)'f', (byte)'t', (byte)'y', (byte)'p',
                    (byte)'i', (byte)'s', (byte)'o', (byte)'m',
                    0, 0, 2, 0,
                    (byte)'m', (byte)'p', (byte)'4', (byte)'2'
                ],
                0x7a)
        ];
    }

    private static int[] ParseUploadSizes()
    {
        var configuredSizes = Environment.GetEnvironmentVariable("DOCKER_E2E_UPLOAD_SIZES_MB")
                              ?? "10,128,512";
        var sizes = configuredSizes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => int.TryParse(value, out var size) && size > 0
                ? size
                : throw new InvalidOperationException(
                    $"DOCKER_E2E_UPLOAD_SIZES_MB contains invalid size '{value}'."))
            .ToArray();
        if (sizes.Length != 3)
        {
            throw new InvalidOperationException("DOCKER_E2E_UPLOAD_SIZES_MB must contain exactly three positive sizes.");
        }

        return sizes;
    }

    private static async Task AssertInitialStateIsCleanAsync(DockerComposeEnvironment environment)
    {
        using var client = environment.CreateHttpClient();
        using var readinessResponse = await client.GetAsync("health/ready");
        Assert.Equal(HttpStatusCode.OK, readinessResponse.StatusCode);

        var feed = XDocument.Parse(await client.GetStringAsync("feed.rss"));
        Assert.Empty(feed.Descendants("item"));

        var blobServiceClient = new BlobServiceClient(environment.BlobConnectionString);
        var exists = await blobServiceClient.GetBlobContainerClient("audiofiles").ExistsAsync();
        Assert.False(exists.Value);
    }

    private static async Task AssertUploadRequiresAuthenticationAsync(DockerComposeEnvironment environment)
    {
        using var client = environment.CreateHttpClient();
        using var response = await client.GetAsync("Home/Upload");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        var redirectTarget = location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;
        Assert.StartsWith("/Identity/Account/Login", redirectTarget);
    }

    private static async Task RegisterAsync(DockerComposeEnvironment environment, string email, string password)
    {
        using var client = environment.CreateHttpClient();
        var token = await GetAntiforgeryTokenAsync(
            client,
            "Identity/Account/Register",
            "form#registerForm");
        using var response = await client.PostAsync(
            "Identity/Account/Register",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Input.Email"] = email,
                ["Input.Password"] = password,
                ["Input.ConfirmPassword"] = password,
                ["__RequestVerificationToken"] = token
            }));

        await AssertRedirectAsync(response, "registration");
    }

    private static async Task LoginAsync(HttpClient client, string email, string password)
    {
        var token = await GetAntiforgeryTokenAsync(
            client,
            "Identity/Account/Login",
            "form#account");
        using var response = await client.PostAsync(
            "Identity/Account/Login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Input.Email"] = email,
                ["Input.Password"] = password,
                ["Input.RememberMe"] = "false",
                ["__RequestVerificationToken"] = token
            }));

        await AssertRedirectAsync(response, "login");
        using var uploadPageResponse = await client.GetAsync("Home/Upload");
        Assert.Equal(HttpStatusCode.OK, uploadPageResponse.StatusCode);
    }

    private static async Task UploadAsync(HttpClient client, UploadSpec uploadSpec)
    {
        var token = await GetAntiforgeryTokenAsync(
            client,
            "Home/Upload",
            "form[enctype='multipart/form-data']");
        using var multipartContent = new MultipartFormDataContent();
        await using var stream = uploadSpec.CreateStream();
        using var fileContent = new StreamContent(stream, 128 * 1024);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(uploadSpec.ContentType);
        fileContent.Headers.ContentLength = uploadSpec.SizeBytes;
        multipartContent.Add(fileContent, "Upload.File", uploadSpec.FileName);
        multipartContent.Add(new StringContent(uploadSpec.Title), "Upload.BookName");
        multipartContent.Add(new StringContent(token), "__RequestVerificationToken");

        using var response = await client.PostAsync("Home/Upload", multipartContent);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = new HtmlParser().ParseDocument(html);
        Assert.Null(document.QuerySelector(".alert-danger"));
    }

    private static async Task AssertUploadPageAsync(
        HttpClient client,
        IReadOnlyList<UploadSpec> uploadSpecs,
        string uploaderEmail)
    {
        var document = new HtmlParser().ParseDocument(await client.GetStringAsync("Home/Upload"));
        var rows = document.QuerySelectorAll("table tbody tr");
        Assert.Equal(uploadSpecs.Count, rows.Length);

        foreach (var uploadSpec in uploadSpecs)
        {
            var row = Assert.Single(
                rows,
                row => NormalizeText(row.QuerySelector(".fw-semibold")) == uploadSpec.Title);
            var cells = row.QuerySelectorAll("td");
            Assert.Equal(5, cells.Length);
            var storagePath = NormalizeText(row.QuerySelector(".small.text-muted"));
            Assert.Contains("/audiofiles/", storagePath);
            Assert.Equal(FormatFileSize(uploadSpec.SizeBytes), NormalizeText(cells[1]));
            Assert.Equal(uploaderEmail, NormalizeText(cells[2]));
            Assert.False(string.IsNullOrWhiteSpace(NormalizeText(cells[3])));
            Assert.NotNull(cells[4].QuerySelector("a.btn-secondary[href]"));
            Assert.NotNull(cells[4].QuerySelector("form[method='post'] button.btn-danger"));
        }
    }

    private static async Task<IReadOnlyList<MediaReference>> AssertFeedAsync(
        DockerComposeEnvironment environment,
        IReadOnlyList<UploadSpec> uploadSpecs)
    {
        using var client = environment.CreateHttpClient();
        var feed = XDocument.Parse(await client.GetStringAsync("feed.rss"));
        var items = feed.Descendants("item").ToList();
        Assert.Equal(uploadSpecs.Count, items.Count);
        var references = new List<MediaReference>();

        foreach (var uploadSpec in uploadSpecs)
        {
            var item = Assert.Single(items, item => item.Element("title")?.Value == uploadSpec.Title);
            var enclosure = Assert.IsType<XElement>(item.Element("enclosure"));
            var enclosureUrl = new Uri(Assert.IsType<XAttribute>(enclosure.Attribute("url")).Value);
            var audioId = Guid.Parse(Assert.IsType<XElement>(item.Element("guid")).Value);
            Assert.Equal(uploadSpec.SizeBytes.ToString(), enclosure.Attribute("length")?.Value);
            Assert.Equal(uploadSpec.ContentType, enclosure.Attribute("type")?.Value);
            Assert.Equal(environment.BaseAddress.Host, enclosureUrl.Host);
            Assert.Equal(environment.BaseAddress.Port, enclosureUrl.Port);
            Assert.EndsWith($"/{audioId}{Path.GetExtension(uploadSpec.FileName)}", enclosureUrl.AbsolutePath);
            references.Add(new MediaReference(uploadSpec, audioId, enclosureUrl));
        }

        return references;
    }

    private static async Task AssertBlobStorageAsync(
        DockerComposeEnvironment environment,
        IReadOnlyList<MediaReference> mediaReferences)
    {
        var blobServiceClient = new BlobServiceClient(environment.BlobConnectionString);
        var containerClient = blobServiceClient.GetBlobContainerClient("audiofiles");
        var blobs = new List<Azure.Storage.Blobs.Models.BlobItem>();
        await foreach (var blob in containerClient.GetBlobsAsync())
        {
            blobs.Add(blob);
        }

        Assert.Equal(mediaReferences.Count, blobs.Count);
        foreach (var mediaReference in mediaReferences)
        {
            var blob = Assert.Single(blobs, blob => blob.Name == mediaReference.AudioId.ToString());
            Assert.Equal(mediaReference.UploadSpec.SizeBytes, blob.Properties.ContentLength);
            Assert.Equal(mediaReference.UploadSpec.ContentType, blob.Properties.ContentType);
        }
    }

    private static async Task AssertMediaResponsesAsync(
        DockerComposeEnvironment environment,
        IReadOnlyList<MediaReference> mediaReferences)
    {
        using var client = environment.CreateHttpClient();

        foreach (var mediaReference in mediaReferences)
        {
            var expectedHash = await ComputeHashAsync(mediaReference.UploadSpec.CreateStream());
            using var fullResponse = await client.GetAsync(
                mediaReference.EnclosureUrl,
                HttpCompletionOption.ResponseHeadersRead);
            Assert.Equal(HttpStatusCode.OK, fullResponse.StatusCode);
            Assert.Equal(mediaReference.UploadSpec.ContentType, fullResponse.Content.Headers.ContentType?.MediaType);
            Assert.Equal(mediaReference.UploadSpec.SizeBytes, fullResponse.Content.Headers.ContentLength);
            await using var fullStream = await fullResponse.Content.ReadAsStreamAsync();
            var (downloadedBytes, actualHash) = await CountAndHashAsync(fullStream);
            Assert.Equal(mediaReference.UploadSpec.SizeBytes, downloadedBytes);
            Assert.Equal(expectedHash, actualHash);

            var rangeStart = mediaReference.UploadSpec.SizeBytes / 2;
            var rangeEnd = rangeStart + 4095;
            using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, mediaReference.EnclosureUrl);
            rangeRequest.Headers.Range = new RangeHeaderValue(rangeStart, rangeEnd);
            using var rangeResponse = await client.SendAsync(rangeRequest);
            Assert.Equal(HttpStatusCode.PartialContent, rangeResponse.StatusCode);
            Assert.Contains("bytes", rangeResponse.Headers.AcceptRanges);
            Assert.Equal(rangeStart, rangeResponse.Content.Headers.ContentRange?.From);
            Assert.Equal(rangeEnd, rangeResponse.Content.Headers.ContentRange?.To);
            Assert.Equal(mediaReference.UploadSpec.SizeBytes, rangeResponse.Content.Headers.ContentRange?.Length);
            var rangeBytes = await rangeResponse.Content.ReadAsByteArrayAsync();
            Assert.Equal(4096, rangeBytes.Length);
            Assert.Equal(mediaReference.UploadSpec.CreateExpectedBytes(rangeStart, rangeBytes.Length), rangeBytes);

            var downloadUri = new UriBuilder(mediaReference.EnclosureUrl)
            {
                Query = "download=true"
            }.Uri;
            using var attachmentRequest = new HttpRequestMessage(HttpMethod.Get, downloadUri);
            attachmentRequest.Headers.Range = new RangeHeaderValue(0, 0);
            using var attachmentResponse = await client.SendAsync(attachmentRequest);
            Assert.Equal(HttpStatusCode.PartialContent, attachmentResponse.StatusCode);
            Assert.Equal("attachment", attachmentResponse.Content.Headers.ContentDisposition?.DispositionType);
            Assert.Contains(
                mediaReference.AudioId.ToString(),
                attachmentResponse.Content.Headers.ContentDisposition?.FileName ?? string.Empty);
        }
    }

    private static async Task<string> GetAntiforgeryTokenAsync(
        HttpClient client,
        string path,
        string formSelector)
    {
        using var response = await client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = new HtmlParser().ParseDocument(html);
        var input = document.QuerySelector($"{formSelector} input[name='__RequestVerificationToken']");

        return input?.GetAttribute("value")
               ?? throw new XunitException($"Could not find antiforgery token on '{path}'.");
    }

    private static async Task AssertRedirectAsync(HttpResponseMessage response, string operation)
    {
        if ((int)response.StatusCode is >= 300 and < 400)
        {
            return;
        }

        throw new XunitException(
            $"Expected {operation} to redirect, but received {(int)response.StatusCode}. " +
            await response.Content.ReadAsStringAsync());
    }

    private static async Task<byte[]> ComputeHashAsync(Stream stream)
    {
        await using (stream)
        using (var sha256 = SHA256.Create())
        {
            return await sha256.ComputeHashAsync(stream);
        }
    }

    private static async Task<(long BytesRead, byte[] Hash)> CountAndHashAsync(Stream stream)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        long bytesRead = 0;

        while (true)
        {
            var count = await stream.ReadAsync(buffer);
            if (count == 0)
            {
                break;
            }

            hash.AppendData(buffer, 0, count);
            bytesRead += count;
        }

        return (bytesRead, hash.GetHashAndReset());
    }

    private static string NormalizeText(IElement? element)
    {
        return element == null
            ? string.Empty
            : string.Join(' ', element.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string FormatFileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var size = (double)bytes;
        var unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return unitIndex == 0 ? $"{bytes} B" : $"{size:0.#} {units[unitIndex]}";
    }

    private sealed record UploadSpec(
        string Title,
        string FileName,
        string ContentType,
        long SizeBytes,
        byte[] Header,
        byte PayloadByte)
    {
        public DeterministicAudioStream CreateStream() => new(SizeBytes, Header, PayloadByte);

        public byte[] CreateExpectedBytes(long offset, int count)
        {
            using var stream = CreateStream();
            stream.Position = offset;
            var bytes = new byte[count];
            stream.ReadExactly(bytes);
            return bytes;
        }
    }

    private sealed record MediaReference(UploadSpec UploadSpec, Guid AudioId, Uri EnclosureUrl);
}