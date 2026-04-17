using DocToSpeech.Core.Extractors;
using DocToSpeech.Core.Transformers;
using DocToSpeech.Core.TTS;

var builder = WebApplication.CreateBuilder(args);

// Register extractors
builder.Services.AddScoped<ITextExtractor, PdfExtractor>();
builder.Services.AddScoped<ITextExtractor, DocxExtractor>();

// Register transformers
builder.Services.AddScoped<TextCleaner>();
builder.Services.AddScoped<TextChunker>();

// Register TTS service
builder.Services.AddScoped<AzureSpeechService>(provider =>
{
    var config = builder.Configuration;
    var key = config["Azure:SpeechKey"];
    var region = config["Azure:SpeechRegion"];
    return new AzureSpeechService(key, region);
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
