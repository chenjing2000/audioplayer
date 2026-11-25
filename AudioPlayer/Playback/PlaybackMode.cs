namespace AudioPlayer.Playback;

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<PlaybackMode>))]
public enum PlaybackMode
{
    PlayOne, RepeatOne, Sequential, RepeatAll
}
