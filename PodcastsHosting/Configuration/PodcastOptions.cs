namespace PodcastsHosting.Configuration;

using System.ComponentModel.DataAnnotations;

public sealed class PodcastOptions
{
    public const string SectionName = "App";

    [Required]
    public required string ChannelTitle { get; init; }

    [Required]
    public required string ChannelDescription { get; init; }

    [Required]
    public required Uri PublicBaseUrl { get; init; }

    public bool RegistrationOpen { get; init; }
}