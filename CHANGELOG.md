# Changelog

Notable changes per version. The version number comes from the `VERSION` file.
Every version has a [GitHub release](https://github.com/RauschIT/WhisperDictation/releases)
with the installer; its release notes are based on the entry here.

## 1.1.1 — 2026-10-18

### Fixed

- Switching from GPU to CPU now frees the graphics memory right away. Before, the
  Whisper model stayed in graphics memory until the app was closed, because the
  Vulkan backend of ggml keeps its GPU device until the process exits.

### Changed

- Whisper runs in its own worker process, `WhisperDictation.Core.exe`, connected
  to the app through a named pipe. Every model or GPU/CPU switch starts a new
  worker and ends the previous one, which releases its RAM and graphics memory
  completely. In Task Manager the worker appears next to `WhisperDictation.App.exe`.
- Setup also closes the worker process on update and uninstall.

## 1.1.0 — 2026-10-10

First public release.

- Whisper `large-v3` (best quality) or `small` (fast), switchable in the window
- GPU (Vulkan) or CPU, switchable in the window
- Types into any application via Unicode input; the clipboard stays untouched
- System-wide hotkey: `Ctrl`+`Space` or double-tap `Ctrl`
- Setup without administrator rights; it downloads the models and verifies them
  by SHA-256
