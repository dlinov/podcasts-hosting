namespace PodcastsHosting.Models
{
    public class AudioModelsViewModel(IList<AudioModel> audioModels)
    {
        public IList<AudioModel> AudioModels { get; } = audioModels;
    }
}