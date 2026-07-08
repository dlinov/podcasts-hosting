namespace PodcastsHosting.Services;

using Microsoft.AspNetCore.Identity;
using PodcastsHosting.Models;

public interface IFileService
{
    Task<List<AudioModel>> ListAllAudios();

    ValueTask<AudioModel?> GetAudioAsync(Guid audioId);

    Task<Stream> OpenAudioReadStreamAsync(Guid audioId);

    Task<bool> DeleteAudioAsync(Guid audioId);

    Task UploadAudioAsync(
        IdentityUser user,
        IFormFile file,
        string bookName,
        string? bookSeries,
        string? chapterTitle,
        int? chapterNumber);
}