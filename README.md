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
once. Then open <http://localhost:5138/swagger> to try it:

| Endpoint | What it does |
|---|---|
| `GET /api/speech/status` | Whether the voice model is ready (shows download progress) |
| `GET /api/speech/voices` | Available voices |
| `POST /api/speech/convert` | Upload a `.pdf`, `.docx`, or `.txt` file → WAV audio |
| `POST /api/speech/text` | Send `{ "text": "...", "voice": "bf_emma", "speed": 1.1 }` → WAV audio |

`voice` and `speed` (0.5–2.0) are optional on both conversion endpoints.

### Speed

Narration runs faster than real time on a typical desktop CPU — about 4–5×
on a 6-core Ryzen 5, so a 10-hour book takes roughly 2 hours.

## Voices

28 English voices are included — American (`af_*`, `am_*`) and British
(`bf_*`, `bm_*`), female and male. The default is `af_heart`. Try
`bf_emma`, `am_michael`, or `bm_george` for variety.

## Configuration

Settings live in `TTSFreeUse.Api/appsettings.json`:

```json
"Tts": {
  "Engine": "Kokoro",
  "Kokoro": {
    "ModelsDirectory": "",
    "DefaultVoice": "af_heart",
    "Threads": 0
  }
}
```

- `ModelsDirectory` — where the voice model is stored. Empty means
  `%LOCALAPPDATA%\TTSFreeUse\models` on Windows or
  `~/.local/share/TTSFreeUse/models` on Linux/macOS.
- `Threads` — CPU threads used for speech; `0` picks automatically.

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
