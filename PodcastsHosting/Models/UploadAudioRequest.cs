namespace PodcastsHosting.Models;

public sealed class UploadAudioRequest
{
    public IFormFile? File { get; set; }

    public string BookName { get; set; } = string.Empty;

    public string? BookSeries { get; set; }

    public string? ChapterTitle { get; set; }

    public int? ChapterNumber { get; set; }
}