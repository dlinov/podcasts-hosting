namespace PodcastsHosting.Controllers;

using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PodcastsHosting.Models;
using PodcastsHosting.Services;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly IAudioService _audioService;
    private readonly PodcastFeedBuilder _podcastFeedBuilder;

    public HomeController(
        ILogger<HomeController> logger,
        UserManager<IdentityUser> userManager,
        IAudioService audioService,
        PodcastFeedBuilder podcastFeedBuilder)
    {
        _logger = logger;
        _userManager = userManager;
        _audioService = audioService;
        _podcastFeedBuilder = podcastFeedBuilder;
    }

    public IActionResult Index()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    [Authorize]
    public async Task<IActionResult> Upload()
    {
        var allAudios = await _audioService.ListAllAudios();
        return View(new AudioModelsViewModel(allAudios));
    }

    [HttpPost]
    [Authorize]
    [RequestSizeLimit(1024L * 1024 * 1024)]
    public async Task<IActionResult> Upload([Bind(Prefix = nameof(AudioModelsViewModel.Upload))] UploadAudioRequest request)
    {
        if (request.File == null || request.File.Length == 0)
        {
            ModelState.AddModelError($"{nameof(AudioModelsViewModel.Upload)}.{nameof(UploadAudioRequest.File)}", "Please upload a file.");
            var allAudios = await _audioService.ListAllAudios();
            return View(new AudioModelsViewModel(allAudios));
        }

        var validationError = await AudioFileValidator.GetValidationErrorAsync(request.File);
        if (validationError != null)
        {
            ModelState.AddModelError($"{nameof(AudioModelsViewModel.Upload)}.{nameof(UploadAudioRequest.File)}", validationError);
            var allAudios = await _audioService.ListAllAudios();
            return View(new AudioModelsViewModel(allAudios));
        }

        var user = await _userManager.GetUserAsync(User);
        if (user != null)
        {
            try
            {
                await _audioService.UploadAudioAsync(
                    user,
                    request.File,
                    request.BookName,
                    request.BookSeries,
                    request.ChapterTitle,
                    request.ChapterNumber);
                ViewBag.Message = "File uploaded successfully.";
                var allAudios = await _audioService.ListAllAudios();
                return View(new AudioModelsViewModel(allAudios));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading file: {Message}. Details: {Details}", ex.Message, ex.ToString());
                ModelState.AddModelError(
                    $"{nameof(AudioModelsViewModel.Upload)}.{nameof(UploadAudioRequest.File)}",
                    "The file could not be uploaded. Please try again later.");
                var allAudios = await _audioService.ListAllAudios();
                return View(new AudioModelsViewModel(allAudios));
            }
        }

        ModelState.AddModelError($"{nameof(AudioModelsViewModel.Upload)}.{nameof(UploadAudioRequest.File)}", "No user found.");
        var audios = await _audioService.ListAllAudios();
        return View(new AudioModelsViewModel(audios));
    }

    [Route("audio/{id:guid}.{extension?}")]
    public async Task<IActionResult> Download(Guid id, bool download = false)
    {
        var audioModel = await _audioService.GetAudioAsync(id);;
        if (audioModel == null)
        {
            return NotFound($"No audio with id {id} was found.");
        }

        try
        {
            var extension = NormalizeExtension(audioModel.Extension);
            var contentType = ChooseContentTypeByExtension(extension);
            var fileDownloadName = $"{id}{extension}";
            var stream = await _audioService.OpenAudioReadStreamAsync(id);

            return download
                ? File(stream, contentType, fileDownloadName, enableRangeProcessing: true)
                : File(stream, contentType, enableRangeProcessing: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file with id {id}", id);
            return StatusCode(500);
        }
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var audioModel = await _audioService.GetAudioAsync(id);
        if (audioModel == null)
        {
            return NotFound($"No audio with id {id} was found.");
        }
        await _audioService.DeleteAudioAsync(id);
        return RedirectToAction("Upload");
    }

    [Route("feed.rss")]
    public async Task<IActionResult> Rss()
    {
        var audioModels = await _audioService.ListAllAudios();
        var rss = _podcastFeedBuilder.Build(audioModels);

        return Content(rss.ToString(), "application/rss+xml", Encoding.UTF8);
    }

    private static string ChooseContentTypeByExtension(string? extension)
    {
        return AudioFormats.GetByExtensionOrDefault(extension).ContentType;
    }

    private static string NormalizeExtension(string? extension)
    {
        return AudioFormats.GetByExtensionOrDefault(extension).Extension;
    }
}
