namespace PodcastsHosting.Controllers;

using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PodcastsHosting.Models;
using PodcastsHosting.Services;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly IConfiguration _configuration;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly IFileService _fileService;

    public HomeController(
        ILogger<HomeController> logger,
        IConfiguration configuration,
        UserManager<IdentityUser> userManager,
        IFileService fileService)
    {
        _logger = logger;
        _configuration = configuration;
        _userManager = userManager;
        _fileService = fileService;
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
        var allAudios = await _fileService.ListAllAudios().ConfigureAwait(false);
        return View(new AudioModelsViewModel(allAudios));
    }

    [HttpPost]
    [Authorize]
    [RequestSizeLimit(512L * 1024 * 1024)]
    public async Task<IActionResult> Upload(
        IFormFile? file,
        string bookName,
        string? bookSeries,
        string? chapterTitle,
        int? chapterNumber)
    {
        if (file == null || file.Length == 0)
        {
            ModelState.AddModelError("File", "Please upload a file.");
            var allAudios = await _fileService.ListAllAudios().ConfigureAwait(false);
            return View(new AudioModelsViewModel(allAudios));
        }

        var validationError = await PodcastsHosting.Services.AudioFileValidator.GetValidationErrorAsync(file);
        if (validationError != null)
        {
            ModelState.AddModelError("File", validationError);
            var allAudios = await _fileService.ListAllAudios().ConfigureAwait(false);
            return View(new AudioModelsViewModel(allAudios));
        }

        var user = await _userManager.GetUserAsync(User);
        if (user != null)
        {
            try
            {
                await _fileService.UploadAudioAsync(user, file, bookName, bookSeries, chapterTitle, chapterNumber);
                ViewBag.Message = "File uploaded successfully.";
                var allAudios = await _fileService.ListAllAudios().ConfigureAwait(false);
                return View(new AudioModelsViewModel(allAudios));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading file: {Message}. Details: {Details}", ex.Message, ex.ToString());
                ModelState.AddModelError("File", "The file could not be uploaded. Please try again later.");
                var allAudios = await _fileService.ListAllAudios().ConfigureAwait(false);
                return View(new AudioModelsViewModel(allAudios));
            }
        }

        ModelState.AddModelError("File", "No user found.");
        var audios = await _fileService.ListAllAudios().ConfigureAwait(false);
        return View(new AudioModelsViewModel(audios));
    }

    [Route("audio/{id:guid}.{extension?}")]
    public async Task<IActionResult> Download(Guid id, bool download = false)
    {
        var audioModel = await _fileService.GetAudioAsync(id);;
        if (audioModel == null)
        {
            return NotFound($"No audio with id {id} was found.");
        }

        try
        {
            var extension = NormalizeExtension(audioModel.Extension);
            var contentType = ChooseContentTypeByExtension(extension);
            var fileDownloadName = $"{id}{extension}";
            var stream = await _fileService.OpenAudioReadStreamAsync(id);

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
        var audioModel = await _fileService.GetAudioAsync(id);
        if (audioModel == null)
        {
            return NotFound($"No audio with id {id} was found.");
        }
        await _fileService.DeleteAudioAsync(id).ConfigureAwait(false);
        return RedirectToAction("Upload");
    }

    [Route("feed.rss")]
    public async Task<IActionResult> Rss()
    {
        var channelTitle = _configuration["App:ChannelTitle"];
        var description = _configuration["App:ChannelDescription"];
        var baseUri = GetPublicBaseUri();
        var audioModels = await _fileService.ListAllAudios().ConfigureAwait(false);
        var itunesNs = XNamespace.Get("http://www.itunes.com/dtds/podcast-1.0.dtd");
        var podcastNs = XNamespace.Get("http://podcastindex.org/namespace/1.0");
        var atomNs = XNamespace.Get("http://www.w3.org/2005/Atom");
        var rss = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("rss",
                new XAttribute("version", "2.0"),
                new XAttribute(XNamespace.Xmlns + "itunes", itunesNs),
                new XAttribute(XNamespace.Xmlns + "podcast", podcastNs),
                new XAttribute(XNamespace.Xmlns + "atom", atomNs),
                new XElement("channel",
                    new XElement(atomNs + "link",
                        new XAttribute("href", $"{baseUri}feed.rss"),
                        new XAttribute("rel", "self"),
                        new XAttribute("type", "application/rss+xml")),
                    new XElement("title", channelTitle),
                    new XElement("link", baseUri),
                    new XElement("description", description),
                    new XElement("language", "ru-ru"),
                    new XElement(itunesNs + "category",
                        new XAttribute("text", "Arts"),
                        new XElement(itunesNs + "category",
                            new XAttribute("text", "Books")
                        )
                    ),
                    new XElement(itunesNs + "explicit", "no"),
                    new XElement(itunesNs + "image", new XAttribute("href", $"{baseUri}images/logo.jpg")),
                    audioModels.Select(audioModel =>
                        new XElement("item",
                            new XElement("title", audioModel.FileName),
                            new XElement("enclosure",
                                new XAttribute("url", new Uri(baseUri, $"audio/{audioModel.Id}{NormalizeExtension(audioModel.Extension)}")),
                                new XAttribute("length", audioModel.FileSize),
                                new XAttribute("type", ChooseContentTypeByExtension(audioModel.Extension))
                            ),
                            new XElement("guid", audioModel.Id),
                            new XElement("pubDate", audioModel.UploadTime.ToString("R"))
                        )
                    )
                )
            )
        );

        return Content(rss.ToString(), "application/rss+xml", Encoding.UTF8);
    }

    private Uri GetPublicBaseUri()
    {
        var publicBaseUrl = _configuration["App:PublicBaseUrl"];
        if (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var publicBaseUri))
        {
            throw new InvalidOperationException("App:PublicBaseUrl must be configured as an absolute URL.");
        }

        return publicBaseUri;
    }

    private static string ChooseContentTypeByExtension(string? extension)
    {
        return NormalizeExtension(extension) switch
        {
            ".mp3" => "audio/mpeg",
            ".m4a" => "audio/mp4",
            ".m4b" => "audio/mp4",
            _ => "audio/mpeg"
        };
    }

    private static string NormalizeExtension(string? extension)
    {
        return string.IsNullOrWhiteSpace(extension)
            ? ".mp3"
            : extension.ToLowerInvariant();
    }
}
