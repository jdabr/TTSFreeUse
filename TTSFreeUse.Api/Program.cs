using TTSFreeUse.Api;
using TTSFreeUse.Core.Audio;
using TTSFreeUse.Core.Extractors;
using TTSFreeUse.Core.Jobs;
using TTSFreeUse.Core.Pipeline;
using TTSFreeUse.Core.Transformers;
using TTSFreeUse.Core.TTS;
using TTSFreeUse.Core.TTS.Kokoro;

var builder = WebApplication.CreateBuilder(args);

// Register extractors
builder.Services.AddSingleton<ITextExtractor, PdfExtractor>();
builder.Services.AddSingleton<ITextExtractor, DocxExtractor>();
builder.Services.AddSingleton<ITextExtractor, PlainTextExtractor>();
builder.Services.AddSingleton<DocumentReader>();

// Register transformers and the narration pipeline
builder.Services.AddSingleton<TextCleaner>();
builder.Services.AddSingleton<TextChunker>();
builder.Services.AddSingleton<NarrationPipeline>();

// Register the TTS engine chosen in configuration ("Tts:Engine"). Kokoro is
// free and offline; Azure is optional and needs an Azure Speech key.
builder.Services.AddSingleton<ITtsEngine>(provider =>
{
    var config = provider.GetRequiredService<IConfiguration>();
    var engine = config["Tts:Engine"] ?? "Kokoro";
    return engine.ToLowerInvariant() switch
    {
        "kokoro" => new KokoroTtsEngine(
            config.GetSection("Tts:Kokoro").Get<KokoroOptions>() ?? new KokoroOptions()),
        "azure" => new AzureTtsEngine(config["Azure:SpeechKey"], config["Azure:SpeechRegion"]),
        _ => throw new InvalidOperationException(
            $"Unknown Tts:Engine '{engine}'. Use \"Kokoro\" or \"Azure\"."),
    };
});

// Download/load the voice model at startup instead of on the first request.
builder.Services.AddHostedService<EngineWarmupService>();

// Background conversion of documents into audio files (MP3/M4B/WAV).
builder.Services.AddSingleton(provider => new Ffmpeg(
    provider.GetRequiredService<IConfiguration>().GetSection("Ffmpeg").Get<FfmpegOptions>() ?? new FfmpegOptions()));
builder.Services.AddSingleton(provider => new AudioLibrary(
    provider.GetRequiredService<IConfiguration>()["Library:Directory"]));
builder.Services.AddSingleton<NarrationJobQueue>();
builder.Services.AddHostedService<JobWorkerService>();

// Accept and return formats as names ("Mp3"), not numbers.
builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// No HTTPS redirection: the app runs locally on the user's own machine
// (http://localhost), where it adds nothing but a startup warning.

// Serves the web page in wwwroot (index.html at "/").
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthorization();
app.MapControllers();

app.Run();
