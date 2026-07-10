namespace PodcastsHosting.Models
{
    public class AudioModelsViewModel(IList<AudioModel> audioModels)
    {
        public UploadAudioRequest Upload { get; set; } = new();

        public IList<AudioModel> AudioModels { get; } = audioModels;
    }
}