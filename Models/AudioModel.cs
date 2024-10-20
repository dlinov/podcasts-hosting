namespace PodcastsHosting.Models;

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

public class AudioModel
{
    [Required]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(255)]
    public string FileName { get; set; }

    [Required]
    [MaxLength(255)]
    public string FilePath { get; set; }

    [Required]
    [MaxLength(255)]
    public string FileHash { get; set; }

    [Required]
    public long FileSize { get; set; }

    [Required]
    public DateTime UploadTime { get; set; }

    [MaxLength(15)]
    public string? Extension { get; set; }

    public IdentityUser? UploadUser { get; set; }

    public string? UploadUserId { get; set; }
}