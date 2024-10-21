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
    private readonly FileService _fileService;

    public HomeController(
        ILogger<HomeController> logger,
        IConfiguration configuration,
        UserManager<IdentityUser> userManager,
        FileService fileService)
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
    [RequestSizeLimit(2L * 1024 * 1024 * 1024)]
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
                _logger.LogError(ex, "Error uploading file");
                ModelState.AddModelError("File", ex.Message);
                var allAudios = await _fileService.ListAllAudios().ConfigureAwait(false);
                return View(new AudioModelsViewModel(allAudios));
            }
        }

        ModelState.AddModelError("File", "No user found.");
        var audios = await _fileService.ListAllAudios().ConfigureAwait(false);
        return View(new AudioModelsViewModel(audios));
    }

    public async Task<IActionResult> Download(Guid id)
    {
        var audioModel = await _fileService.GetAudioAsync(id);;
        if (audioModel == null)
        {
            return NotFound($"No audio with id {id} was found.");
        }

        try
        {
            var extension = audioModel.Extension ?? ".mp3";
            var contentType = ChooseContentTypeByExtension(extension);
            await using var stream = await _fileService.DownloadAudioAsync(id);

            return File(stream, contentType, $"{id}{extension}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file with id {id}", id);
            return StatusCode(500);
        }
    }

    [Authorize]
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
        var protocol = Request.IsHttps ? "https" : "http";
        var baseUri = new Uri($"{protocol}://{Request.Host.ToUriComponent()}");
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
                                new XAttribute("url", $"{baseUri}Home/Download/{audioModel.Id}"),
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

    private static string ChooseContentTypeByExtension(string? extension)
    {
        return extension switch
        {
            ".mp3" => "audio/mpeg",
            ".m4a" => "audio/m4a",
            ".m4b" => "audio/m4b", // not sure apple podcasts support this
            _ => "audio/mpeg"
        };
    }
}
