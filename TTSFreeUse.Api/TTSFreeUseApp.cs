using TTSFreeUse.Core.Audio;
using TTSFreeUse.Core.Extractors;
using TTSFreeUse.Core.Jobs;
using TTSFreeUse.Core.Pipeline;
using TTSFreeUse.Core.Pronunciation;
using TTSFreeUse.Core.Transformers;
using TTSFreeUse.Core.TTS;
using TTSFreeUse.Core.TTS.Kokoro;

namespace TTSFreeUse.Api
{
    /// <summary>
    /// Builds the TTSFreeUse web app. Used by <c>dotnet run</c> (Program.cs)
    /// and by the Windows launcher (TTSFreeUse.Desktop), so both behave the same.
    /// </summary>
    public static class TTSFreeUseApp
    {
        public const int DefaultPort = 5138;

        public static WebApplication Create(string[] args, WebApplicationOptions? options = null)
        {
            var builder = WebApplication.CreateBuilder(options ?? new WebApplicationOptions { Args = args });
            var services = builder.Services;

            // Text extraction
            services.AddSingleton<ITextExtractor, PdfExtractor>();
            services.AddSingleton<ITextExtractor, DocxExtractor>();
            services.AddSingleton<ITextExtractor, PlainTextExtractor>();
            services.AddSingleton<DocumentReader>();

            // Cleanup, chunking, and the narration pipeline
            services.AddSingleton<TextCleaner>();
            services.AddSingleton<TextChunker>();
            services.AddSingleton<NarrationPipeline>();

            // How technical text is spoken (acronyms, units, references), plus the
            // user's own pronunciation list saved on this computer.
            services.AddSingleton(provider => new PronunciationStore(
                provider.GetRequiredService<IConfiguration>()["Pronunciations:File"]));
            services.AddSingleton(provider => new SpeechNormalizer(
                provider.GetRequiredService<PronunciationStore>(),
                (provider.GetRequiredService<ITtsEngine>() as KokoroTtsEngine) is { } kokoro ? kokoro.IsKnownWord : null));

            // The voice engine chosen in configuration ("Tts:Engine"). Kokoro is
            // free and offline; Azure is optional, needs an Azure Speech key,
            // and is only available when running from source.
            services.AddSingleton<ITtsEngine>(provider =>
            {
                var config = provider.GetRequiredService<IConfiguration>();
                var engine = config["Tts:Engine"] ?? "Kokoro";
                return engine.ToLowerInvariant() switch
                {
                    "kokoro" => new KokoroTtsEngine(
                        config.GetSection("Tts:Kokoro").Get<KokoroOptions>() ?? new KokoroOptions()),
#if AZURE
                    "azure" => new Azure.AzureTtsEngine(config["Azure:SpeechKey"], config["Azure:SpeechRegion"]),
#else
                    "azure" => throw new InvalidOperationException(
                        "Azure voices aren't included in this version of TTSFreeUse. Set Tts:Engine to " +
                        "\"Kokoro\", or run TTSFreeUse from source to use Azure (see README.md)."),
#endif
                    _ => throw new InvalidOperationException(
                        $"Unknown Tts:Engine '{engine}'. Use \"Kokoro\" or \"Azure\"."),
                };
            });

            // Download/load the voice model at startup instead of on the first request.
            services.AddHostedService<EngineWarmupService>();

            // Background conversion of documents into audio files (MP3/M4B/WAV).
            services.AddSingleton(provider => new Ffmpeg(
                provider.GetRequiredService<IConfiguration>().GetSection("Ffmpeg").Get<FfmpegOptions>() ?? new FfmpegOptions()));
            services.AddSingleton(provider => new AudioLibrary(
                provider.GetRequiredService<IConfiguration>()["Library:Directory"]));
            services.AddSingleton<NarrationJobQueue>();
            services.AddHostedService<JobWorkerService>();

            // Accept and return formats as names ("Mp3"), not numbers. The
            // controllers live in this assembly, which may not be the entry
            // assembly (the Windows launcher hosts this app).
            services.AddControllers()
                .AddApplicationPart(typeof(TTSFreeUseApp).Assembly)
                .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(
                    new System.Text.Json.Serialization.JsonStringEnumConverter()));
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen();

            var app = builder.Build();

            // API docs at /swagger. The app only listens on this computer
            // (localhost), so they're always on.
            app.UseSwagger();
            app.UseSwaggerUI();

            // No HTTPS redirection: the app runs locally on the user's own machine
            // (http://localhost), where it adds nothing but a startup warning.

            // Serves the web page in wwwroot (index.html at "/").
            app.UseDefaultFiles();
            app.UseStaticFiles();

            app.UseAuthorization();
            app.MapControllers();
            return app;
        }
    }
}
