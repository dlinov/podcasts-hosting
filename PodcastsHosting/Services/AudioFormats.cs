namespace PodcastsHosting.Services;

public enum AudioSignatureKind
{
    Mp3,
    Aac,
    IsoBaseMedia
}

public sealed record AudioFormat(
    string DisplayName,
    string Extension,
    string ContentType,
    AudioSignatureKind SignatureKind,
    IReadOnlySet<string> AcceptedContentTypes);

public static class AudioFormats
{
    public static AudioFormat Mp3 { get; } = Create(
        "MP3",
        ".mp3",
        "audio/mpeg",
        AudioSignatureKind.Mp3,
        "audio/mp3",
        "audio/x-mp3",
        "audio/x-mpeg");

    public static AudioFormat Aac { get; } = Create(
        "AAC",
        ".aac",
        "audio/aac",
        AudioSignatureKind.Aac,
        "audio/aacp",
        "audio/x-aac");

    public static AudioFormat M4a { get; } = Create(
        "M4A",
        ".m4a",
        "audio/mp4",
        AudioSignatureKind.IsoBaseMedia,
        "application/mp4",
        "audio/m4a",
        "audio/x-m4a");

    public static AudioFormat M4b { get; } = Create(
        "M4B",
        ".m4b",
        "audio/mp4",
        AudioSignatureKind.IsoBaseMedia,
        "application/mp4",
        "audio/m4b",
        "audio/x-m4b");

    private static readonly AudioFormat[] SupportedFormats = [Mp3, Aac, M4a, M4b];
    private static readonly IReadOnlyDictionary<string, AudioFormat> FormatsByExtension = SupportedFormats
        .ToDictionary(format => format.Extension, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<AudioFormat> Supported => SupportedFormats;

    public static string SupportedDisplayNames { get; } = FormatDisplayNames();

    public static string FileInputAccept { get; } = string.Join(",",
        SupportedFormats.Select(format => format.Extension)
            .Concat(SupportedFormats.Select(format => format.ContentType))
            .Distinct(StringComparer.OrdinalIgnoreCase));

    public static AudioFormat? FindByExtension(string? extension)
    {
        return !string.IsNullOrWhiteSpace(extension) && FormatsByExtension.TryGetValue(extension, out var format)
            ? format
            : null;
    }

    public static AudioFormat GetByExtensionOrDefault(string? extension)
    {
        return FindByExtension(extension) ?? Mp3;
    }

    public static bool IsSupportedUploadContentType(string contentType)
    {
        return contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
               || contentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase)
               || SupportedFormats.Any(format => format.AcceptedContentTypes.Contains(contentType));
    }

    private static AudioFormat Create(
        string displayName,
        string extension,
        string contentType,
        AudioSignatureKind signatureKind,
        params string[] alternateContentTypes)
    {
        var acceptedContentTypes = new HashSet<string>(alternateContentTypes, StringComparer.OrdinalIgnoreCase)
        {
            contentType
        };

        return new AudioFormat(displayName, extension, contentType, signatureKind, acceptedContentTypes);
    }

    private static string FormatDisplayNames()
    {
        return SupportedFormats.Length switch
        {
            0 => string.Empty,
            1 => SupportedFormats[0].DisplayName,
            2 => $"{SupportedFormats[0].DisplayName} and {SupportedFormats[1].DisplayName}",
            _ => $"{string.Join(", ", SupportedFormats[..^1].Select(format => format.DisplayName))}, and {SupportedFormats[^1].DisplayName}"
        };
    }
}