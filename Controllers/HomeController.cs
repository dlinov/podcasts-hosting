namespace PodcastsHosting.Controllers;

using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PodcastsHosting.Data;
using PodcastsHosting.Models;

public class HomeController : Controller
{
    private const string AccountName = "podcasthostingstorage";
    private const string ContainerName = "audiofiles";
    private readonly ILogger<HomeController> _logger;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly string _connectionString;

    public HomeController(
        ILogger<HomeController> logger,
        UserManager<IdentityUser> userManager,
        ApplicationDbContext dbContext,
        IConfiguration configuration)
    {
        _logger = logger;
        _userManager = userManager;
        _dbContext = dbContext;
        _configuration = configuration;
        _connectionString = _configuration["Storage:ConnectionString"];
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
        var allAudios = await _dbContext.AudioModels.OrderBy(x => x.UploadTime).ToListAsync();
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
        // TODO: move this to the place where it's needed/extract method - it's called twice in happy flow
        var allAudios = await _dbContext.AudioModels.OrderBy(x => x.UploadTime).ToListAsync();
        if (file == null || file.Length == 0)
        {
            ModelState.AddModelError("File", "Please upload a file.");
            return View(new AudioModelsViewModel(allAudios));
        }

        var user = await _userManager.GetUserAsync(User);

        // TODO: move the logic to a service from here
        if (user != null)
        {
            try
            {
                var audioId = Guid.NewGuid();
                var extension = Path.GetExtension(file.FileName);
                var customTitle = BuildTitle(bookName, bookSeries, chapterTitle, chapterNumber);

                var blobClient = await BuildBlobClientAsync(audioId);
                await using var stream = file.OpenReadStream();
                var resp = await blobClient.UploadAsync(stream, true);
                var blobHash = Convert.ToBase64String(resp.Value.ContentHash);

                var audioModel = new AudioModel
                {
                    Id = audioId,
                    FileName = customTitle.ToString(),
                    FilePath = blobClient.Uri.ToString(),
                    FileSize = file.Length,
                    FileHash = blobHash,
                    Extension = extension,
                    UploadTime = DateTime.UtcNow,
                    UploadUser = user
                };

                _dbContext.AudioModels.Add(audioModel);
                await _dbContext.SaveChangesAsync(); // TODO: remove file from storage if failed

                allAudios = await _dbContext.AudioModels.OrderBy(x => x.UploadTime).ToListAsync();
                ViewBag.Message = "File uploaded successfully.";
                _logger.LogInformation("File {audioModel.FileName} uploaded successfully by {user.Email}", audioModel.FileName, user.Email);

                return View(new AudioModelsViewModel(allAudios));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading file");
                ModelState.AddModelError("File", ex.Message);
                return View(new AudioModelsViewModel(allAudios));
            }
        }
        else
        {
            ModelState.AddModelError("File", "No user found.");
            return View(new AudioModelsViewModel(allAudios));
        }
    }

    public async Task<IActionResult> Download(Guid id)
    {
        var audioModel = await _dbContext.AudioModels.FindAsync(id);
        if (audioModel == null)
        {
            return NotFound($"No audio with id {id} was found.");
        }

        try
        {
            var extension = audioModel.Extension ?? ".mp3";
            var contentType = ChooseContentTypeByExtension(extension);
            var blobClient = await BuildBlobClientAsync(id);
            var blobDownloadInfo = await blobClient.DownloadAsync();
            await using var stream = blobDownloadInfo.Value.Content;

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
        var audioModel = await _dbContext.AudioModels.FindAsync(id);
        if (audioModel == null)
        {
            return NotFound();
        }

        var blobClient = await BuildBlobClientAsync(id);
        await blobClient.DeleteIfExistsAsync();

        _dbContext.AudioModels.Remove(audioModel);
        await _dbContext.SaveChangesAsync();

        return RedirectToAction("Upload");
    }

    [Route("feed.rss")]
    public async Task<IActionResult> Rss()
    {
        var channelTitle = _configuration["App:ChannelTitle"];
        var description = _configuration["App:ChannelDescription"];
        var protocol = Request.IsHttps ? "https" : "http";
        var baseUri = new Uri($"{protocol}://{Request.Host.ToUriComponent()}");
        var audioModels = await _dbContext.AudioModels.OrderBy(x => x.UploadTime).ToListAsync();
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

    private async Task<BlobClient> BuildBlobClientAsync(Guid audioId)
    {
        var blobServiceClient = new BlobServiceClient(_connectionString);
        var containerClient = blobServiceClient.GetBlobContainerClient(ContainerName);
        await containerClient.CreateIfNotExistsAsync();
        var blobClient = containerClient.GetBlobClient(audioId.ToString());
        return blobClient;
    }

    private static StringBuilder BuildTitle(
        string bookName,
        string? bookSeries,
        string? chapterTitle,
        int? chapterNumber)
    {
        var customTitle = new StringBuilder(bookName);

        if (!string.IsNullOrWhiteSpace(bookSeries))
        {
            customTitle.Append($" [{bookSeries}]");
        }

        if (!string.IsNullOrWhiteSpace(chapterTitle))
        {
            if (chapterNumber != null)
            {
                customTitle.Append($" | {chapterNumber} {chapterTitle}");
            }
            else
            {
                customTitle.Append($" | {chapterTitle}");
            }
        }

        return customTitle;
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
