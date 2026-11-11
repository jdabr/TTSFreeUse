namespace TTSFreeUse.Core.Audio
{
    /// <summary>
    /// Temporary files for audio being made. Only the current user can read
    /// them: on Linux the temp folder is shared by everyone on the computer,
    /// and new files there are readable by all users unless created private.
    /// </summary>
    public static class TempAudioFile
    {
        /// <summary>
        /// Creates "ttsfreeuse-<paramref name="name"/>" in the temp folder, failing
        /// if it already exists, and returns it open for reading and writing.
        /// </summary>
        public static FileStream Create(string name, FileOptions options = FileOptions.None)
        {
            var settings = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.ReadWrite,
                Share = FileShare.Read,
                Options = options,
                BufferSize = 81920,
            };
            // Windows temp folders are already per-user.
            if (!OperatingSystem.IsWindows())
                settings.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            return new FileStream(Path.Combine(Path.GetTempPath(), "ttsfreeuse-" + name), settings);
        }
    }
}
