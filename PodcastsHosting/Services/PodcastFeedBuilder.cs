namespace PodcastsHosting.Services;

using System.Xml.Linq;
using Microsoft.Extensions.Options;
using PodcastsHosting.Configuration;
using PodcastsHosting.Models;

public sealed class PodcastFeedBuilder
{
    private static readonly XNamespace ItunesNamespace = XNamespace.Get("http://www.itunes.com/dtds/podcast-1.0.dtd");
    private static readonly XNamespace PodcastNamespace = XNamespace.Get("http://podcastindex.org/namespace/1.0");
    private static readonly XNamespace AtomNamespace = XNamespace.Get("http://www.w3.org/2005/Atom");
    private readonly PodcastOptions _options;

    public PodcastFeedBuilder(IOptions<PodcastOptions> options)
    {
        _options = options.Value;
    }

    public XDocument Build(IEnumerable<AudioModel> audioModels)
    {
        var baseUri = EnsureTrailingSlash(_options.PublicBaseUrl);

        return new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("rss",
                new XAttribute("version", "2.0"),
                new XAttribute(XNamespace.Xmlns + "itunes", ItunesNamespace),
                new XAttribute(XNamespace.Xmlns + "podcast", PodcastNamespace),
                new XAttribute(XNamespace.Xmlns + "atom", AtomNamespace),
                new XElement("channel",
                    new XElement(AtomNamespace + "link",
                        new XAttribute("href", new Uri(baseUri, "feed.rss")),
                        new XAttribute("rel", "self"),
                        new XAttribute("type", "application/rss+xml")),
                    new XElement("title", _options.ChannelTitle),
                    new XElement("link", baseUri),
                    new XElement("description", _options.ChannelDescription),
                    new XElement("language", "ru-ru"),
                    new XElement(ItunesNamespace + "category",
                        new XAttribute("text", "Arts"),
                        new XElement(ItunesNamespace + "category",
                            new XAttribute("text", "Books"))),
                    new XElement(ItunesNamespace + "explicit", "no"),
                    new XElement(ItunesNamespace + "image",
                        new XAttribute("href", new Uri(baseUri, "images/logo.jpg"))),
                    audioModels.Select(audioModel => BuildItem(baseUri, audioModel)))));
    }

    private static XElement BuildItem(Uri baseUri, AudioModel audioModel)
    {
        var format = AudioFormats.GetByExtensionOrDefault(audioModel.Extension);

        return new XElement("item",
            new XElement("title", audioModel.FileName),
            new XElement("enclosure",
                new XAttribute("url", new Uri(baseUri, $"audio/{audioModel.Id}{format.Extension}")),
                new XAttribute("length", audioModel.FileSize),
                new XAttribute("type", format.ContentType)),
            new XElement("guid", audioModel.Id),
            new XElement("pubDate", audioModel.UploadTime.ToString("R")));
    }

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        return uri.AbsoluteUri.EndsWith('/')
            ? uri
            : new Uri($"{uri.AbsoluteUri}/");
    }
}