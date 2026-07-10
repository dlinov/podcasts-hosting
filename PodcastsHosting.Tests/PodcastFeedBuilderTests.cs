namespace PodcastsHosting.Tests;

using System.Xml.Linq;
using Microsoft.Extensions.Options;
using PodcastsHosting.Configuration;
using PodcastsHosting.Models;
using PodcastsHosting.Services;

public class PodcastFeedBuilderTests
{
    [Fact]
    public void Build_UsesConfiguredMetadataAndCanonicalAudioFormat()
    {
        var audioId = Guid.NewGuid();
        var builder = new PodcastFeedBuilder(Options.Create(new PodcastOptions
        {
            ChannelTitle = "Test Podcast",
            ChannelDescription = "Test description",
            PublicBaseUrl = new Uri("https://podcasts.example")
        }));
        var audio = new AudioModel
        {
            Id = audioId,
            FileName = "Episode",
            FilePath = "https://storage.example/episode",
            FileHash = "hash",
            FileSize = 42,
            UploadTime = DateTimeOffset.Parse("2026-07-10T12:00:00Z").UtcDateTime,
            Extension = ".m4b"
        };

        var document = builder.Build([audio]);

        var channel = Assert.IsType<XElement>(document.Root?.Element("channel"));
        Assert.Equal("Test Podcast", channel.Element("title")?.Value);
        Assert.Equal("Test description", channel.Element("description")?.Value);
        var atomNamespace = XNamespace.Get("http://www.w3.org/2005/Atom");
        Assert.Equal("https://podcasts.example/feed.rss", channel.Element(atomNamespace + "link")?.Attribute("href")?.Value);
        var enclosure = Assert.Single(channel.Descendants("enclosure"));
        Assert.Equal($"https://podcasts.example/audio/{audioId}.m4b", enclosure.Attribute("url")?.Value);
        Assert.Equal("audio/mp4", enclosure.Attribute("type")?.Value);
        Assert.Equal("42", enclosure.Attribute("length")?.Value);
    }
}