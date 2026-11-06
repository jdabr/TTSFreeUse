# TTSFreeUse

Turn books and documents into audio. TTSFreeUse extracts the text from a PDF or
DOCX file, cleans it up for listening, and reads it aloud as an MP3 — handy for
technical and professional books that have no audiobook edition.

> **Status:** early prototype. Speech currently uses Azure Cognitive Services;
> a free, offline voice engine is the next planned step.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- An Azure Speech resource (key + region)

## Setup

1. Clone the repository.
2. Store your Azure credentials as user secrets (they stay out of git):
   ```sh
   cd TTSFreeUse.Api
   dotnet user-secrets set "Azure:SpeechKey" "<your key>"
   dotnet user-secrets set "Azure:SpeechRegion" "<your region, e.g. eastus>"
   ```
   In Visual Studio you can instead right-click `TTSFreeUse.Api` → **Manage User Secrets**.
3. Run the API:
   ```sh
   dotnet run --project TTSFreeUse.Api
   ```
4. Open `http://localhost:5138/swagger`, use `POST /api/speech/convert`, and
   upload a `.pdf` or `.docx` file to get back an MP3.

## Project layout

| Project | Purpose |
|---|---|
| `TTSFreeUse.Core` | Text extraction (PDF, DOCX), cleanup, chunking, and speech synthesis |
| `TTSFreeUse.Api` | ASP.NET Core web API exposing the conversion endpoint |

## License

TTSFreeUse is free and open source under the [MIT License](LICENSE).

Note that the optional Azure Speech SDK (`Microsoft.CognitiveServices.Speech`)
is distributed by Microsoft under its own license terms, not MIT.
