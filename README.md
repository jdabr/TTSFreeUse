# TTSFreeUse

Listen to your documents instead of reading them — free, offline, and private.
TTSFreeUse reads PDF, DOCX, and TXT files (or text you paste in) aloud with a
natural-sounding voice. It's built for people who can't read print or find
reading difficult, such as people with dyslexia, low vision, or limited
literacy — for example:

- **Tradespeople studying for certifications** who want to listen to manuals
  and study guides on the drive to work or on the job site.
- **Actors with dyslexia** learning a script by ear.
- **Anyone** who understands better by listening than by reading.

What it offers:

- **Free:** no account, no API key, no usage limits.
- **Offline & private:** speech is generated on your own computer by the
  open-source [Kokoro](https://huggingface.co/hexgrad/Kokoro-82M) voice model.
  Your documents never leave your machine.
- **Book-aware cleanup:** removes running headers and page numbers, rejoins
  words hyphenated across lines, and fixes PDF ligatures and smart quotes.
- **Reads Word equations aloud:** "x squared plus y squared equals z
  squared", "a over b", "the square root of x". (Equations in PDFs aren't
  understood yet.)

> **Status:** early development. It currently runs as a local web API; a
> simple app interface and one-click installers are planned.

## Quick start

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```sh
git clone https://github.com/jdabr/TTSFreeUse.git
cd TTSFreeUse
dotnet run --project TTSFreeUse.Api
```

On first run the voice model (~330 MB) downloads automatically — this happens
once. Then open <http://localhost:5138>:

- **Listen now** starts reading pasted text or an uploaded document within a
  few seconds, showing the passage being read, with pause and stop.
- **Save audio file** (or **Upload file**) to turn text or a PDF/DOCX/TXT
  document into an **MP3** (plays everywhere) or **M4B** (audiobook apps
  remember your place). Long documents convert in the background with a
  progress bar.
- Finished files are saved to **Documents\TTSFreeUse**, where you can play,
  download, or copy them to your phone.

### Audio converter (FFmpeg)

MP3 and M4B files are made with [FFmpeg](https://ffmpeg.org).

- **Windows:** downloaded automatically the first time you save an MP3/M4B
  (~110 MB, once). TTSFreeUse downloads a fixed version (9.0.2, from
  [gyan.dev](https://www.gyan.dev/ffmpeg/builds/)) and verifies its SHA-256
  checksum before using it.
- **macOS / Linux:** install it yourself — `brew install ffmpeg` or
  `sudo apt install ffmpeg` — then restart TTSFreeUse.
- If you already have FFmpeg installed, TTSFreeUse uses it.
- WAV output needs nothing extra (but is large: ~170 MB per hour).

### API

The web page uses a small HTTP API, also browsable at
<http://localhost:5138/swagger>:

| Endpoint | What it does |
|---|---|
| `GET /api/speech/status` | Whether the voice model is ready (shows download progress) |
| `GET /api/speech/voices` | Available voices |
| `POST /api/speech/prepare` | `{ "text": "..." }` → the cleaned text split into short passages for live listening |
| `POST /api/speech/prepare-file` | Same, for an uploaded `.pdf`, `.docx`, or `.txt` file |
| `POST /api/speech/text` | Instant: `{ "text": "...", "voice": "bf_emma", "speed": 1.1 }` → WAV audio |
| `POST /api/speech/convert` | Instant: upload a `.pdf`, `.docx`, or `.txt` file → WAV audio |
| `POST /api/jobs/text` | Background: `{ "text": "...", "format": "Mp3" }` → job |
| `POST /api/jobs/file` | Background: upload a document (+ `format`, `voice`, `speed`) → job |
| `GET /api/jobs`, `GET /api/jobs/{id}` | Job progress and estimated time left |
| `POST /api/jobs/{id}/cancel` | Cancel a job |
| `GET /api/library` | Saved audio files |
| `GET /api/library/{file}` | Play (supports seeking) or `?download=true` |
| `DELETE /api/library/{file}` | Delete a saved file |

`voice`, `speed` (0.5–2.0), and `format` (`Mp3`, `M4b`, `Wav`) are optional.

### Speed

Narration runs faster than real time on a typical desktop CPU — about 4–5×
on a 6-core Ryzen 5, so a 10-hour book takes roughly 2 hours.

## Voices

The voice menu shows the 11 most natural-sounding English voices, using the
quality grades published by Kokoro's author in
[VOICES.md](https://huggingface.co/hexgrad/Kokoro-82M/blob/main/VOICES.md):

| Accent | Female | Male |
|---|---|---|
| American | Heart (A, default), Bella (A-), Nicole (B-), Aoede, Kore, Sarah (C+) | Michael, Fenrir, Puck (C+) |
| British | Emma (B-) | George (C) |

The model has 17 more English voices, but they were trained on only a few
minutes of audio and often sound robotic. To list them anyway, set
`Tts:Kokoro:ShowAllVoices` to `true`.

## Configuration

Settings live in `TTSFreeUse.Api/appsettings.json`:

```json
"Tts": {
  "Engine": "Kokoro",
  "Kokoro": {
    "ModelsDirectory": "",
    "DefaultVoice": "af_heart",
    "Threads": 0,
    "ShowAllVoices": false
  }
}
```

- `ModelsDirectory` — where the voice model is stored. Empty means
  `%LOCALAPPDATA%\TTSFreeUse\models` on Windows or
  `~/.local/share/TTSFreeUse/models` on Linux/macOS.
- `Threads` — CPU threads used for speech; `0` picks automatically.
- `Library:Directory` — where saved audio goes; empty means
  `Documents\TTSFreeUse`.
- `Ffmpeg:Path` — use a specific `ffmpeg` executable;
  `Ffmpeg:AllowDownload` — set to `false` to never download FFmpeg.

### Optional: Azure cloud voices

If you have an Azure AI Speech resource, you can use its voices instead. Set
`"Engine": "Azure"` and store your credentials as user secrets so they stay
out of git:

```sh
cd TTSFreeUse.Api
dotnet user-secrets set "Azure:SpeechKey" "<your key>"
dotnet user-secrets set "Azure:SpeechRegion" "<your region, e.g. eastus>"
```

Azure is a paid service and sends your text to Microsoft; Kokoro needs neither.

## Project layout

| Project | Purpose |
|---|---|
| `TTSFreeUse.Core` | Text extraction, cleanup, chunking, voice engines, and audio output |
| `TTSFreeUse.Api` | ASP.NET Core web API exposing the conversion endpoints |
| `TTSFreeUse.Core.Tests` | Unit tests (`dotnet test`) |

## Responsible use

TTSFreeUse is an accessibility tool. Please use it with documents you have the
right to use: your own files, materials provided by your employer, school, or
training program, books you've purchased, and public-domain works (such as
those on [Project Gutenberg](https://www.gutenberg.org)). Don't use it with
pirated books, and don't share the audio it creates from copyrighted works.

TTSFreeUse respects copy protection and will not bypass it:

- Password-protected and DRM-protected files are refused.
- For PDFs with restrictions, TTSFreeUse follows the publisher's
  **"text access for accessibility"** setting, which is the permission
  publishers use specifically for screen readers and other assistive tools.
- Everything runs on your own computer; nothing is uploaded or shared.

If a book you need isn't readable, ask the publisher for an accessible edition
(many provide one on request), or check services for people with print
disabilities such as [Bookshare](https://www.bookshare.org) or your national
library for the blind.

## Contributing

Contributions are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md).

## License

TTSFreeUse's own code is free and open source under the [MIT License](LICENSE).
It depends on third-party components under their own licenses, including
espeak-ng (GPL-3.0), which is built into the speech library — see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
