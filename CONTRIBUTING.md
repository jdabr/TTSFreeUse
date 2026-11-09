# Contributing to TTSFreeUse

Thanks for your interest in helping! TTSFreeUse aims to make any book listenable,
for free, on your own computer. Bug reports, ideas, and pull requests are all
welcome.

## Reporting problems

Open an issue on GitHub. For text that's read incorrectly (skipped words,
headers read aloud, odd pronunciations), it helps a lot to include:

- the file type (PDF, DOCX, TXT) and, if you can share it, a short sample that
  shows the problem — please only share documents you have the right to share;
- what was read aloud versus what you expected.

## Development setup

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
2. Clone the repo and build:
   ```sh
   git clone https://github.com/jdabr/TTSFreeUse.git
   cd TTSFreeUse
   dotnet build
   ```
3. Run the tests:
   ```sh
   dotnet test
   ```
   One test performs real speech synthesis and is skipped by default because
   it needs the ~330 MB voice model. To include it, point
   `TTSFREEUSE_MODELS_DIR` at a folder that already contains the model (for
   example `%LOCALAPPDATA%\TTSFreeUse\models` after running the app once):
   ```sh
   TTSFREEUSE_MODELS_DIR="$LOCALAPPDATA/TTSFreeUse/models" dotnet test
   ```
   Likewise, the FFmpeg conversion tests run only when `TTSFREEUSE_FFMPEG`
   points at an ffmpeg executable:
   ```sh
   TTSFREEUSE_FFMPEG="$LOCALAPPDATA/TTSFreeUse/tools/ffmpeg-9.0.2/ffmpeg.exe" dotnet test
   ```
4. Run the app with `dotnet run --project TTSFreeUse.Api` and open
   <http://localhost:5138> (API docs at `/swagger`).

## Building the Windows installer

```powershell
winget install JRSoftware.InnoSetup      # once
.\installer\build-installer.ps1 -Version 0.1.0
```

This publishes `TTSFreeUse.Desktop` self-contained (no .NET needed on the
user's machine) and writes `artifacts\TTSFreeUse-Setup-0.1.0.exe`. To test the
app without installing, run `artifacts\publish\win-x64\TTSFreeUse.exe`
(`--port 5199 --no-browser` avoids clashing with a `dotnet run` copy).

Releases are built by GitHub Actions: pushing a tag like `v0.1.0` runs the
tests, builds the installer, and attaches it to a new GitHub release
(`.github/workflows/release.yml`).

## How the code fits together

```
file ──► ITextExtractor ──► TextCleaner ──► TextChunker ──► SpeechNormalizer ──► ITtsEngine ──► WavWriter
         (PDF/DOCX/TXT)     (clean up)      (≤800 chars)    (GFCI → G F C I)     (Kokoro/Azure)  (audio)
```

`SpeechNormalizer` (in `TTSFreeUse.Core/Transformers`) rewrites each chunk
just before synthesis — acronyms, units, fractions, references, symbols, then
the user's `PronunciationStore` list on top. Displayed text is never changed.
If a technical term is read wrong, a new rule with a test case in
`SpeechNormalizerTests` is the most useful kind of contribution.

`NarrationPipeline` (in `TTSFreeUse.Core/Pipeline`) ties these together, so
any front end — the web API today, a desktop app or CLI later — reuses it.

### Adding a file format

Implement `ITextExtractor` in `TTSFreeUse.Core/Extractors`, register it in
`TTSFreeUse.Api/Program.cs`, and add tests in `TTSFreeUse.Core.Tests`.
Extractors should keep paragraph breaks as blank lines (`\n\n`); the cleaner
handles the rest.

If the format supports encryption or DRM, the extractor must detect protected
files and throw `ProtectedDocumentException` with a plain-language message
(see `PdfExtractor` and `DocxExtractor`), with tests using protected samples.

### Adding a voice engine

Implement `ITtsEngine` in `TTSFreeUse.Core/TTS`. Engines return mono PCM
samples as floats in [-1, 1] at their `SampleRate`, so audio from any engine
is written the same way. Add a case for it in `TTSFreeUse.Api/TTSFreeUseApp.cs`.

Everything shipped in the Windows installer must be open source (an
OSI-approved license). An engine that needs a closed-source SDK goes in its
own project and is left out of installer builds, like `TTSFreeUse.Azure`
(see `IncludeAzure` in `Directory.Build.props`). The installer build script
fails if closed-source Azure files end up in the package.

## Project principles

TTSFreeUse exists to help people who can't read print, or find reading hard,
access material they're entitled to. To keep it that way, the project will
**not** accept features that make copyright infringement easier, including:

- removing, bypassing, or weakening DRM, passwords, or PDF permission
  settings — protected files must be refused with a clear message;
- searching for or downloading books from unauthorized sources, or linking to
  them (public-domain sources such as Project Gutenberg are welcome);
- uploading, publishing, or sharing generated audio with others;
- descriptions or marketing that pitch the tool as a way to get "free
  audiobooks" of commercial books.

Features that serve readers directly are the priority: clearer pronunciation,
read-along highlighting, easier navigation, and support for the kinds of
documents people study and work with.

## Pull requests

- Keep changes focused; one feature or fix per PR.
- Add or update tests for behavior changes, and make sure `dotnet test` passes.
- Match the existing code style (the build should have no warnings).
- New dependencies must have a license compatible with MIT distribution;
  mention them in `THIRD-PARTY-NOTICES.md`.

By contributing, you agree that your contributions are licensed under the
project's [MIT License](LICENSE).
