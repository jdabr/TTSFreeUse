using System.Buffers.Binary;

namespace TTSFreeUse.Core.Audio
{
    /// <summary>
    /// Streams 16-bit mono PCM audio to a WAV file. Samples are appended as they
    /// are produced, so a whole book never has to fit in memory; the header's
    /// size fields are filled in on <see cref="DisposeAsync"/>.
    /// </summary>
    public sealed class WavWriter : IAsyncDisposable
    {
        private const int HeaderSize = 44;
        private const short BitsPerSample = 16;
        private const short Channels = 1;

        private readonly Stream _stream;
        private readonly bool _leaveOpen;
        private long _dataBytes;
        private bool _finished;

        public int SampleRate { get; }

        public TimeSpan Duration =>
            TimeSpan.FromSeconds((double)_dataBytes / (SampleRate * Channels * BitsPerSample / 8));

        /// <param name="stream">Must be seekable so the header can be finalized.</param>
        public WavWriter(Stream stream, int sampleRate, bool leaveOpen = false)
        {
            if (!stream.CanSeek || !stream.CanWrite)
                throw new ArgumentException("WAV output stream must be writable and seekable.", nameof(stream));

            _stream = stream;
            _leaveOpen = leaveOpen;
            SampleRate = sampleRate;
            _stream.Write(BuildHeader(0)); // placeholder sizes
        }

        public async Task WriteAsync(ReadOnlyMemory<float> samples, CancellationToken cancellationToken = default)
        {
            var buffer = new byte[samples.Length * 2];
            var span = samples.Span;
            for (int i = 0; i < span.Length; i++)
            {
                var clamped = Math.Clamp(span[i], -1f, 1f);
                BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(i * 2), (short)(clamped * short.MaxValue));
            }
            await _stream.WriteAsync(buffer, cancellationToken);
            _dataBytes += buffer.Length;
        }

        public Task WriteSilenceAsync(TimeSpan duration, CancellationToken cancellationToken = default) =>
            WriteAsync(new float[(int)(duration.TotalSeconds * SampleRate)], cancellationToken);

        /// <summary>Writes the final sizes into the header. Called automatically on dispose.</summary>
        public async Task FinishAsync()
        {
            if (_finished)
                return;
            _finished = true;

            var end = _stream.Position;
            _stream.Seek(0, SeekOrigin.Begin);
            await _stream.WriteAsync(BuildHeader(_dataBytes));
            _stream.Seek(end, SeekOrigin.Begin);
            await _stream.FlushAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await FinishAsync();
            if (!_leaveOpen)
                await _stream.DisposeAsync();
        }

        private byte[] BuildHeader(long dataBytes)
        {
            // WAV sizes are 32-bit; ~12 hours of 24 kHz audio fits. Longer
            // output is clamped (most players still read it to the end).
            var data = (uint)Math.Min(dataBytes, uint.MaxValue - HeaderSize);
            var blockAlign = (short)(Channels * BitsPerSample / 8);

            var h = new byte[HeaderSize];
            "RIFF"u8.CopyTo(h);
            BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(4), data + HeaderSize - 8);
            "WAVE"u8.CopyTo(h.AsSpan(8));
            "fmt "u8.CopyTo(h.AsSpan(12));
            BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(16), 16);        // fmt chunk size
            BinaryPrimitives.WriteInt16LittleEndian(h.AsSpan(20), 1);         // PCM
            BinaryPrimitives.WriteInt16LittleEndian(h.AsSpan(22), Channels);
            BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(24), SampleRate);
            BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(28), SampleRate * blockAlign);
            BinaryPrimitives.WriteInt16LittleEndian(h.AsSpan(32), blockAlign);
            BinaryPrimitives.WriteInt16LittleEndian(h.AsSpan(34), BitsPerSample);
            "data"u8.CopyTo(h.AsSpan(36));
            BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(40), data);
            return h;
        }
    }
}
