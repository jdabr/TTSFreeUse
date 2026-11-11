using SherpaOnnx;

namespace TTSFreeUse.Core.TTS.Kokoro
{
    public class KokoroOptions
    {
        /// <summary>Where models are stored; empty = per-user default folder.</summary>
        public string? ModelsDirectory { get; set; }

        public string DefaultVoice { get; set; } = "af_heart";

        /// <summary>CPU threads for synthesis; 0 = choose automatically.</summary>
        public int Threads { get; set; }

        /// <summary>
        /// List all 28 English voices instead of only the most natural-sounding
        /// ones. The lower-graded voices are trained on little audio and can
        /// sound robotic or mispronounce words.
        /// </summary>
        public bool ShowAllVoices { get; set; }
    }

    /// <summary>
    /// Free, offline text-to-speech using the Kokoro-82M model (Apache-2.0)
    /// through sherpa-onnx. Runs on the CPU; no account or internet needed
    /// after the one-time model download.
    /// </summary>
    public sealed class KokoroTtsEngine : ITtsEngine, IDisposable
    {
        // Speaker IDs are fixed by the model (see the model's speaker_names
        // metadata). Phase 1 exposes the English voices, IDs 0-27.
        private static readonly string[] EnglishVoiceIds =
        {
            "af_alloy", "af_aoede", "af_bella", "af_heart", "af_jessica", "af_kore", "af_nicole",
            "af_nova", "af_river", "af_sarah", "af_sky", "am_adam", "am_echo", "am_eric",
            "am_fenrir", "am_liam", "am_michael", "am_onyx", "am_puck", "am_santa", "bf_alice",
            "bf_emma", "bf_isabella", "bf_lily", "bm_daniel", "bm_fable", "bm_george", "bm_lewis",
        };

        // The voices shown by default, best first within each accent/gender.
        // Based on the grades in Kokoro's VOICES.md
        // (https://huggingface.co/hexgrad/Kokoro-82M/blob/main/VOICES.md), which
        // reflect how much clean training audio each voice had: we keep C+ and
        // above, plus George (C) so there is a British male voice.
        internal static readonly string[] RecommendedVoiceIds =
        {
            "af_heart",   // A
            "af_bella",   // A-
            "af_nicole",  // B-
            "af_aoede",   // C+
            "af_kore",    // C+
            "af_sarah",   // C+
            "am_michael", // C+
            "am_fenrir",  // C+
            "am_puck",    // C+
            "bf_emma",    // B-
            "bm_george",  // C
        };

        private static TtsVoice ToVoice(string id) => new(
            Id: id,
            Name: char.ToUpperInvariant(id[3]) + id[4..],
            Language: id[0] == 'b' ? "en-GB" : "en-US",
            Gender: id[1] == 'f' ? "female" : "male");

        private readonly IReadOnlyList<TtsVoice> _voices;

        private readonly KokoroModel _model;
        private readonly KokoroOptions _options;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private readonly SemaphoreSlim _synthLock = new(1, 1);
        private volatile OfflineTts? _tts;
        private volatile bool _disposed;
        private volatile string _status = "Not started.";

        public KokoroTtsEngine(KokoroOptions options)
        {
            _options = options;
            _model = new KokoroModel(options.ModelsDirectory);
            if (!EnglishVoiceIds.Contains(options.DefaultVoice))
                throw new ArgumentException($"Unknown Kokoro voice '{options.DefaultVoice}'.");

            // A configured default outside the recommended set is still listed.
            var ids = options.ShowAllVoices
                ? EnglishVoiceIds
                : RecommendedVoiceIds.Contains(options.DefaultVoice)
                    ? RecommendedVoiceIds
                    : RecommendedVoiceIds.Append(options.DefaultVoice).ToArray();
            _voices = ids.Select(ToVoice).ToList();
        }

        public string Name => "kokoro";
        public int SampleRate => 24000;
        public IReadOnlyList<TtsVoice> Voices => _voices;
        public string DefaultVoice => _options.DefaultVoice;
        public bool IsReady => _tts != null;
        public string Status => _status;

        private Lazy<HashSet<string>?>? _lexicon;

        /// <summary>
        /// Whether a lowercase word is in the voice's pronunciation dictionary,
        /// or null before the model is downloaded. Used to tell a heading word
        /// in capitals ("WARNING") from an acronym to spell out ("GFCI").
        /// </summary>
        public bool? IsKnownWord(string word)
        {
            if (!IsReady)
                return null;
            _lexicon ??= new Lazy<HashSet<string>?>(LoadLexiconWords);
            return _lexicon.Value?.Contains(word);
        }

        private HashSet<string>? LoadLexiconWords()
        {
            try
            {
                // Each line is "word phoneme phoneme …".
                var words = new HashSet<string>(StringComparer.Ordinal);
                foreach (var line in File.ReadLines(_model.LexiconFile))
                {
                    var space = line.IndexOf(' ');
                    if (space > 0)
                        words.Add(line[..space]);
                }
                return words;
            }
            catch (IOException)
            {
                return null;
            }
        }

        /// <summary>
        /// Downloads the model if needed (~330 MB, first run only) and loads it.
        /// Safe to call more than once.
        /// </summary>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_tts != null)
                return;

            await _initLock.WaitAsync(cancellationToken);
            try
            {
                if (_tts != null || _disposed)
                    return;

                if (!_model.IsInstalled())
                {
                    _status = "Downloading voice model (first run only)…";
                    var progress = new Progress<double>(p =>
                        _status = $"Downloading voice model (first run only): {p:P0}");
                    await _model.EnsureInstalledAsync(progress, cancellationToken: cancellationToken);
                }

                _status = "Loading voice model…";
                _tts = await Task.Run(() => new OfflineTts(BuildConfig()), cancellationToken);
                _status = "Ready.";
            }
            // Anything but the caller giving up, including a download timing out.
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _status = $"Voice model failed to load: {ex.Message}";
                throw;
            }
            finally
            {
                _initLock.Release();
            }
        }

        public async Task<float[]> SynthesizeAsync(string text, TtsOptions options,
            CancellationToken cancellationToken = default)
        {
            // Checked before anything reaches the native engine, which fails on
            // bad input in ways .NET can't always catch.
            if (!TtsOptions.IsValidSpeed(options.Speed))
                throw new ArgumentOutOfRangeException(nameof(options), options.Speed,
                    $"Speed must be between {TtsOptions.MinSpeed} and {TtsOptions.MaxSpeed}.");
            var voiceId = options.Voice ?? DefaultVoice;
            var speakerId = Array.IndexOf(EnglishVoiceIds, voiceId);
            if (speakerId < 0)
                throw new ArgumentException($"Unknown voice '{voiceId}'.", nameof(options));

            await InitializeAsync(cancellationToken);

            // One synthesis at a time: the model already uses multiple threads,
            // and running several at once only adds memory pressure.
            await _synthLock.WaitAsync(cancellationToken);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var tts = _tts!;
                return await Task.Run(() =>
                {
                    // OfflineTtsGeneratedAudio has Dispose() but isn't IDisposable.
                    var audio = tts.Generate(text, options.Speed, speakerId);
                    try { return audio.Samples; }
                    finally { audio.Dispose(); }
                }, cancellationToken);
            }
            finally
            {
                _synthLock.Release();
            }
        }

        private OfflineTtsConfig BuildConfig()
        {
            var config = new OfflineTtsConfig();
            config.Model.Kokoro.Model = _model.ModelFile;
            config.Model.Kokoro.Voices = _model.VoicesFile;
            config.Model.Kokoro.Tokens = _model.TokensFile;
            config.Model.Kokoro.Lexicon = _model.LexiconFile;
            config.Model.Kokoro.DataDir = _model.EspeakDataDir; // pronunciation fallback for unknown words
            config.Model.Kokoro.LengthScale = 1.0f;
            // Measured: speed keeps improving up to ~8 threads, then levels off.
            config.Model.NumThreads = _options.Threads > 0
                ? _options.Threads
                : Math.Clamp(Environment.ProcessorCount * 2 / 3, 1, 8);
            config.Model.Provider = "cpu";
            config.MaxNumSentences = 1;
            return config;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            // Speech in progress can't be interrupted, and freeing the native
            // engine under it crashes the app. So if someone quits while
            // TTSFreeUse is still reading aloud, the engine is left for the
            // operating system to free when the process exits.
            if (_synthLock.Wait(TimeSpan.Zero))
            {
                _tts?.Dispose();
                _tts = null;
                _synthLock.Release();
            }
        }
    }
}
