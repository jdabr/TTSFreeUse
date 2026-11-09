# Third-party notices

TTSFreeUse's own source code is licensed under the [MIT License](LICENSE).
It uses the following third-party components, each under its own license.

## Libraries (NuGet packages)

| Component | License | Used for |
|---|---|---|
| [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) (`org.k2fsa.sherpa.onnx`) | Apache-2.0 | Running the Kokoro voice model |
| [ONNX Runtime](https://github.com/microsoft/onnxruntime) (bundled with sherpa-onnx) | MIT | Neural network inference |
| [espeak-ng](https://github.com/espeak-ng/espeak-ng) (compiled into sherpa-onnx) | GPL-3.0-or-later | Pronouncing words missing from the lexicon |
| [PdfPig](https://github.com/UglyToad/PdfPig) | Apache-2.0 | PDF text extraction |
| [Open XML SDK](https://github.com/dotnet/Open-XML-SDK) (`DocumentFormat.OpenXml`) | MIT | DOCX text extraction |
| [SharpZipLib](https://github.com/icsharpcode/SharpZipLib) | MIT | Unpacking the downloaded voice model |
| [Swashbuckle.AspNetCore](https://github.com/domaindrivendev/Swashbuckle.AspNetCore) | MIT | API documentation page |
| [Azure Speech SDK](https://aka.ms/csspeech) (`Microsoft.CognitiveServices.Speech`) | [Microsoft Software License](https://aka.ms/csspeech/license) (not open source) | Optional Azure cloud voices — **source builds only; not included in the Windows installer** |

## Included in the Windows installer

| Component | License |
|---|---|
| [.NET runtime](https://github.com/dotnet/runtime), ASP.NET Core, and Windows Forms (bundled so .NET doesn't need to be installed) | MIT |
| [Inno Setup](https://jrsoftware.org/isinfo.php) (the installer itself) | [Inno Setup License](https://jrsoftware.org/files/is/license.txt) |

## Voice model (downloaded on first run)

| Component | License |
|---|---|
| [Kokoro-82M](https://huggingface.co/hexgrad/Kokoro-82M) v1.0, packaged by sherpa-onnx (`kokoro-multi-lang-v1_0`) | Apache-2.0 |
| espeak-ng pronunciation data (`espeak-ng-data`, included in the model download) | GPL-3.0-or-later |

The model is not stored in this repository; it is downloaded from the
sherpa-onnx GitHub releases the first time the app runs.

## Audio converter (downloaded on first use, Windows)

| Component | License |
|---|---|
| [FFmpeg](https://ffmpeg.org) 9.0.2 "essentials" build by [gyan.dev](https://www.gyan.dev/ffmpeg/builds/), from its [GitHub mirror](https://github.com/GyanD/codexffmpeg/releases/tag/9.0.2) | GPL-3.0 (this build includes GPL components such as libx264) |

FFmpeg is not bundled with TTSFreeUse. It is run as a separate program to
convert audio. On Windows it is downloaded on first use, its SHA-256 is
verified, and its `LICENSE` file is kept next to it in
`%LOCALAPPDATA%\TTSFreeUse\tools`. On macOS and Linux the system's FFmpeg is
used. FFmpeg's source code is available at <https://ffmpeg.org/download.html>.

## Note on espeak-ng (GPL-3.0)

espeak-ng is built into the sherpa-onnx native library that TTSFreeUse loads.
TTSFreeUse's MIT-licensed code is compatible with the GPL, but **binary
distributions that bundle sherpa-onnx (for example, packaged installers) are,
taken as a whole, subject to the terms of GPL-3.0**: recipients must be able to
obtain the corresponding source code. Because TTSFreeUse and all of its
components are open source, this is satisfied by linking to their source
repositories in each release.

This file is provided for convenience and is not legal advice.
