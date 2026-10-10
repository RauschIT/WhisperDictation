# Third-Party Notices

This project bundles or depends on the third-party components listed below.
Each component remains under its own licence and its own copyright. The MIT
licence in `LICENSE` applies only to the work of Rausch IT.

All components listed here are permissively licensed (MIT or BSD-3-Clause).
No component is copyleft.

---

## 1. Bundled binaries

These files are part of this repository and are redistributed with it.

### 1.1 WebRtcVadSharp

| | |
|---|---|
| File | `WhisperDictation.Core/lib/WebRtcVadSharp/WebRtcVadSharp.dll` |
| Version | 1.3.2 |
| SHA-256 | `2EB9BF05D234CE946C5828CDB564E9070E331EB3F944E976ADEF9D144B8895DF` |
| Origin | https://github.com/ladenedge/WebRtcVadSharp |
| Licence | MIT |
| Copyright | Copyright (c) 2020 Jay Miller |

### 1.2 WebRtcVad (native library)

| | |
|---|---|
| File | `WhisperDictation.Core/lib/WebRtcVadSharp/WebRtcVad.dll` |
| Version | 1.3.2 |
| SHA-256 | `87157040576D0F90EEB97B422C1EC363201F27AF5336EBC71ADF5A183520F442` |
| Origin | https://github.com/ladenedge/WebRtcVadSharp (native part) |
| Licence | BSD-3-Clause |
| Copyright | Copyright (c) 2011, The WebRTC project authors |

The MIT licence of WebRtcVadSharp covers the managed .NET adapter. The native
`WebRtcVad.dll` contains the WebRTC voice activity detection code, which is
distributed under BSD-3-Clause by the WebRTC project. The upstream licence file
of WebRtcVadSharp carries both notices; both are reproduced in full in section 4.

---

## 2. Source code derived from third-party work

### 2.1 VadSharp

| | |
|---|---|
| File | `WhisperDictation.Core/Services/SileroVadOnnxModel.cs` |
| Origin | https://github.com/GabryB03/VadSharp |
| Licence | MIT |
| Copyright | Copyright (c) 2025 Gabriele Bologna |

This file is a derivative work. It is an adapted version of `VadOnnxModel.cs`
from VadSharp, reduced to single-frame real-time evaluation. The helper methods
`Concatenate` and `GetLastColumns`, the ONNX tensor plumbing and the state/context
bookkeeping are taken from that file; the input validation, resampling, model hash
check and DirectML support were removed, and the remaining code was renamed and
extended.

Because this file carries third-party expression, it deliberately does **not**
carry a Rausch IT copyright header. The MIT notice of Gabriele Bologna applies to
it and is reproduced in full in section 4.

---

## 3. NuGet package dependencies

Restored at build time from nuget.org; not stored in this repository.

### 3.1 Direct dependencies

| Package | Version | Licence | Copyright |
|---|---|---|---|
| Whisper.net | 1.9.0 | MIT | Copyright (c) 2024 sandrohanea |
| Whisper.net.Runtime | 1.9.0 | MIT | Copyright (c) 2024 sandrohanea |
| Whisper.net.Runtime.Vulkan | 1.9.0 | MIT | Copyright (c) 2024 sandrohanea |
| NAudio | 2.3.0 | MIT | Copyright (c) 2001-2025 Mark Heath |
| Microsoft.ML.OnnxRuntime | 1.20.0 | MIT | Copyright (c) Microsoft Corporation |
| Microsoft.Extensions.Logging | 10.0.5 | MIT | Copyright (c) .NET Foundation and Contributors |
| Microsoft.Extensions.Logging.Abstractions | 10.0.5 | MIT | Copyright (c) .NET Foundation and Contributors |
| Microsoft.Extensions.Logging.Console | 10.0.5 | MIT | Copyright (c) .NET Foundation and Contributors |
| Microsoft.Extensions.Logging.Debug | 10.0.5 | MIT | Copyright (c) .NET Foundation and Contributors |

### 3.2 Transitive dependencies

Pulled in automatically by the packages above. All MIT.

| Package | Version |
|---|---|
| Microsoft.Extensions.AI.Abstractions | 10.0.0 |
| Microsoft.Extensions.Configuration | 10.0.5 |
| Microsoft.Extensions.Configuration.Abstractions | 10.0.5 |
| Microsoft.Extensions.Configuration.Binder | 10.0.5 |
| Microsoft.Extensions.DependencyInjection | 10.0.5 |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.5 |
| Microsoft.Extensions.Logging.Configuration | 10.0.5 |
| Microsoft.Extensions.Options | 10.0.5 |
| Microsoft.Extensions.Options.ConfigurationExtensions | 10.0.5 |
| Microsoft.Extensions.Primitives | 10.0.5 |
| Microsoft.ML.OnnxRuntime.Managed | 1.20.0 |
| NAudio.Asio | 2.3.0 |
| NAudio.Core | 2.3.0 |
| NAudio.Midi | 2.3.0 |
| NAudio.Wasapi | 2.3.0 |
| NAudio.WinForms | 2.3.0 |
| NAudio.WinMM | 2.3.0 |

### 3.3 Native libraries inside the NuGet packages

These ship as native binaries inside the packages listed above and are copied
into the build output.

| Native library | Delivered by | Licence | Copyright |
|---|---|---|---|
| `whisper.dll`, `ggml-whisper.dll`, `ggml-base-whisper.dll`, `ggml-cpu-whisper.dll`, `ggml-vulkan-whisper.dll` | Whisper.net.Runtime, Whisper.net.Runtime.Vulkan | MIT | Copyright (c) 2023-2026 The ggml authors (whisper.cpp / ggml) |
| `onnxruntime.dll`, `onnxruntime_providers_shared.dll` | Microsoft.ML.OnnxRuntime | MIT | Copyright (c) Microsoft Corporation |

`Whisper.net.Runtime.Metal` appears in the restore graph. It is a macOS/iOS
runtime pack and contributes no binaries to a Windows build.

---

## 4. Model files

The model files are **not** part of this repository. They are large binary files
that each user downloads separately; the download links are in `README.md` and in
`WhisperDictation.Core/models/README.md`. That README is part of the repository; the
`.bin` and `.onnx` files are excluded by `WhisperDictation.Core/models/.gitignore` and by
the root `.gitignore`. They are listed here for completeness.

| Model | Origin | Licence |
|---|---|---|
| `ggml-large-v3.bin` | https://huggingface.co/ggerganov/whisper.cpp | MIT (whisper.cpp / ggml model set) |
| `ggml-small.bin` | https://huggingface.co/ggerganov/whisper.cpp | MIT (whisper.cpp / ggml model set) |
| `silero_vad.onnx` | https://github.com/snakers4/silero-vad | MIT, Copyright (c) 2020-present Silero Team |

The Whisper model weights originate from OpenAI's Whisper and were converted to
the GGML format by the whisper.cpp project. The Silero VAD model and its
repository are published by the Silero Team under MIT.

---

## 5. Hallucination phrase filter

`WhisperDictation.Core/DictationManager.cs` contains a regular expression with a list of
typical Whisper silence hallucinations (for example "Untertitel von Stephanie
Gündel", "Untertitel im Auftrag des ZDF", "Vielen Dank fürs Zuschauen").

This list is a widely circulated community compilation of artefacts that Whisper
produces on silence. It is documented publicly, among others in the whisper.cpp
and OpenAI Whisper issue trackers. No single licensed upstream source could be
identified for this list. The filter contains no third-party source code; it is a
list of literal strings used to discard known-bad recognition output.

---

## 6. Full licence texts

### 6.1 MIT — WebRtcVadSharp

```
The MIT License (MIT)

Copyright (c) 2020 Jay Miller

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### 6.2 BSD-3-Clause — WebRTC VAD (native)

```
Copyright (c) 2011, The WebRTC project authors. All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are
met:

  * Redistributions of source code must retain the above copyright
    notice, this list of conditions and the following disclaimer.

  * Redistributions in binary form must reproduce the above copyright
    notice, this list of conditions and the following disclaimer in the
    documentation and/or other materials provided with the distribution.

  * Neither the name of Google nor the names of its contributors may
    be used to endorse or promote products derived from this software
    without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

### 6.3 MIT — VadSharp

```
MIT License

Copyright (c) 2025 Gabriele Bologna

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### 6.4 MIT — whisper.cpp / ggml and Whisper.net

```
MIT License

Copyright (c) 2023-2026 The ggml authors
Copyright (c) 2024 sandrohanea (Whisper.net)
Copyright (c) 2020-present Silero Team (Silero VAD)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### 6.5 MIT — .NET Foundation and Contributors, Microsoft Corporation

```
MIT License

Copyright (c) .NET Foundation and Contributors
Copyright (c) Microsoft Corporation
Copyright (c) 2001-2025 Mark Heath (NAudio)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
