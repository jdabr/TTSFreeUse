# DocToSpeech

A web app that extracts text from PDF/DOCX files and converts it to speech.

## Setup

1. Clone the repository
2. Copy `appsettings.example.json` to `appsettings.json`
3. Right-click `DocToSpeech.Api` → Manage User Secrets
4. Add your Azure credentials:
```json
{
  "Azure": {
    "SpeechKey": "your key here",
    "SpeechRegion": "your region here"
  }
}
```
5. Run the project — Swagger will open at https://localhost:[port]/swagger

## Requirements
- Visual Studio 2022
- .NET 8 SDK
- Azure Cognitive Services Speech resource