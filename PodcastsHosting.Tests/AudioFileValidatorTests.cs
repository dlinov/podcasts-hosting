namespace PodcastsHosting.Tests;

using Microsoft.AspNetCore.Http;
using PodcastsHosting.Services;

public class AudioFileValidatorTests
{
    [Theory]
    [InlineData("episode.mp3", "audio/mpeg", new byte[] { (byte)'I', (byte)'D', (byte)'3', 4 })]
    [InlineData("episode.mp3", "audio/mpeg", new byte[] { 0xff, 0xfb, 0x90, 0x64 })]
    [InlineData("episode.aac", "audio/aac", new byte[] { 0xff, 0xf1, 0x50, 0x80 })]
    [InlineData("episode.aac", "audio/vnd.dlna.adts", new byte[] { 0xff, 0xf1, 0x50, 0x80 })]
    [InlineData("episode.aac", "audio/aac", new byte[] { (byte)'A', (byte)'D', (byte)'I', (byte)'F' })]
    [InlineData("episode.m4a", "audio/mp4", new byte[] { 0, 0, 0, 24, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'M', (byte)'4', (byte)'A', (byte)' ' })]
    [InlineData("episode.m4b", "audio/mp4", new byte[] { 0, 0, 0, 24, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'M', (byte)'4', (byte)'B', (byte)' ' })]
    [InlineData("episode.m4a", "audio/mp4", new byte[] { 0, 0, 0, 20, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m', 0, 0, 2, 0, (byte)'m', (byte)'p', (byte)'4', (byte)'2' })]
    [InlineData("episode.m4b", "audio/mp4", new byte[] { 0, 0, 0, 20, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'m', (byte)'p', (byte)'4', (byte)'2', 0, 0, 0, 0, (byte)'i', (byte)'s', (byte)'o', (byte)'2' })]
    public async Task GetValidationErrorAsync_AcceptsSupportedAudioFiles(string fileName, string contentType, byte[] header)
    {
        var file = CreateFormFile(header, fileName, contentType);

        var validationError = await AudioFileValidator.GetValidationErrorAsync(file);

        Assert.Null(validationError);
    }

    [Fact]
    public async Task GetValidationErrorAsync_RejectsUnsupportedExtension()
    {
        var file = CreateFormFile([(byte)'I', (byte)'D', (byte)'3'], "episode.wav", "audio/wav");

        var validationError = await AudioFileValidator.GetValidationErrorAsync(file);

        Assert.Equal("Only MP3, AAC, M4A, and M4B audio files are supported.", validationError);
    }

    [Fact]
    public async Task GetValidationErrorAsync_RejectsUnsupportedContentType()
    {
        var file = CreateFormFile([(byte)'I', (byte)'D', (byte)'3'], "episode.mp3", "text/plain");

        var validationError = await AudioFileValidator.GetValidationErrorAsync(file);

        Assert.Equal("The uploaded file content type is not supported.", validationError);
    }

    [Fact]
    public async Task GetValidationErrorAsync_RejectsContentThatDoesNotMatchExtension()
    {
        var file = CreateFormFile([(byte)'<', (byte)'h', (byte)'t', (byte)'m', (byte)'l'], "episode.mp3", "audio/mpeg");

        var validationError = await AudioFileValidator.GetValidationErrorAsync(file);

        Assert.Equal("The uploaded file does not look like a supported audio file.", validationError);
    }

    [Fact]
    public async Task GetValidationErrorAsync_RejectsUnknownIsoBaseMediaBrand()
    {
        var header = new byte[]
        {
            0, 0, 0, 16,
            (byte)'f', (byte)'t', (byte)'y', (byte)'p',
            (byte)'a', (byte)'v', (byte)'c', (byte)'1',
            0, 0, 0, 0
        };
        var file = CreateFormFile(header, "episode.m4a", "audio/mp4");

        var validationError = await AudioFileValidator.GetValidationErrorAsync(file);

        Assert.Equal("The uploaded file does not look like a supported audio file.", validationError);
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
}