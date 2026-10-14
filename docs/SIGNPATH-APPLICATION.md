# Данные для заявки SignPath Foundation

Заявка ещё не отправлена. Публичные сведения подготовлены; регистрация аккаунта и подтверждение контакта требуют действий владельца.

| Поле | Значение |
| --- | --- |
| Project name | ReplayLite |
| Maintainer / GitHub owner | DraimCiDo |
| Repository | https://github.com/DraimCiDo/ReplayLite |
| Website / documentation | https://github.com/DraimCiDo/ReplayLite#readme |
| Releases | https://github.com/DraimCiDo/ReplayLite/releases |
| License of own code | MIT: https://github.com/DraimCiDo/ReplayLite/blob/main/LICENSE |
| Platform | Windows 10/11 x64, .NET Framework 4.8 |
| Build pipeline | https://github.com/DraimCiDo/ReplayLite/actions/workflows/build-release.yml |
| Contact email | Владелец указывает и подтверждает самостоятельно |

## Project description

ReplayLite is a Windows replay-buffer application. It captures a user-selected desktop monitor through FFmpeg Desktop Duplication and NVIDIA NVENC, optionally with system audio and microphone input through NAudio/WASAPI. Recording starts only when the user enables the buffer. A configurable keyboard shortcut saves recent footage to a local MP4 without re-encoding. Encoded segments are kept in a bounded disk buffer. The application includes a per-user installer, a portable package and an uninstaller. Updates are checked manually against the project's GitHub Releases. Recordings are not uploaded; no telemetry is collected.

## Artifacts requested for signing

Own binaries: `ReplayLite.exe`, `UpdateHelper.exe`, `Uninstall.exe` and `ReplayLite-Setup.exe`. The application's own sources and build scripts are published in the repository. The build downloads pinned upstream FFmpeg/NAudio packages, validates SHA-256 and includes their license information and source archives as documented in THIRD-PARTY.md. Upstream binaries are included unchanged and are not requested for re-signing.

## Следующий шаг владельца

Открыть [заявку SignPath Foundation](https://signpath.org/apply.html), зарегистрировать аккаунт, подтвердить контакт и подать проект на рассмотрение. Перед отправкой проверить условия Foundation и перечисленные выше сведения. После одобрения — заполнить настройки из [SIGNING.md](SIGNING.md). Само наличие MIT и CI не заменяет одобрение сервиса.
