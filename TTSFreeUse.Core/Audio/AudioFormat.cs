namespace TTSFreeUse.Core.Audio
{
    public enum AudioFormat
    {
        /// <summary>Plays on virtually everything: phones, car stereos, computers.</summary>
        Mp3,

        /// <summary>Audiobook file: audiobook apps remember your place in it.</summary>
        M4b,

        /// <summary>Uncompressed; large (~170 MB per hour). No converter needed.</summary>
        Wav,
    }

    public static class AudioFormats
    {
        public static string Extension(this AudioFormat format) => format switch
        {
            AudioFormat.Mp3 => ".mp3",
            AudioFormat.M4b => ".m4b",
            _ => ".wav",
        };

        public static string ContentType(this AudioFormat format) => format switch
        {
            AudioFormat.Mp3 => "audio/mpeg",
            AudioFormat.M4b => "audio/mp4",
            _ => "audio/wav",
        };

        /// <summary>Whether producing this format requires FFmpeg.</summary>
        public static bool NeedsEncoder(this AudioFormat format) => format != AudioFormat.Wav;

        public static bool TryFromExtension(string extension, out AudioFormat format)
        {
            foreach (var f in Enum.GetValues<AudioFormat>())
            {
                if (string.Equals(f.Extension(), extension, StringComparison.OrdinalIgnoreCase))
                {
                    format = f;
                    return true;
                }
            }
            format = default;
            return false;
        }
    }

    /// <summary>Tags written into the audio file so players show a sensible title.</summary>
    public record AudioMetadata(string Title, string? Comment = null);
}
