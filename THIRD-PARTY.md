# Компоненты

## FFmpeg

`bin/ffmpeg.exe` и `bin/ffprobe.exe`: FFmpeg **9.0.2**, сборка essentials от Gyan, GCC 16.2.0, GPL v3-or-later. Copyright © 2000–2026 FFmpeg developers и участники включённых библиотек.

Оригинальная сборка: https://github.com/GyanD/codexffmpeg/releases/tag/9.0.2

SHA256 оригинального ZIP, сверенный с контрольной суммой издателя: `60F467265B1E312373DBCD92200C2618A74850F98D3D078E94296BB3FA2047BA`.

Текст лицензии: `bin/FFmpeg-LICENSE.txt`; сведения издателя: `bin/FFmpeg-README.txt`; точные параметры сборки: `bin/FFmpeg-build.txt`.

Исходники FFmpeg для указанного издателем commit `946fcce07b`: https://github.com/FFmpeg/FFmpeg/commit/946fcce07b . В локальном переносимом архиве они также находятся в `third-party-source/FFmpeg-946fcce07b.zip`; в Git архивы зависимостей не включены.

Сведения о вариантах сборки и включённых внешних библиотеках: https://www.gyan.dev/ffmpeg/builds/ . Включённый архив исходников FFmpeg не включает исходники всех внешних библиотек статической сборки.

## NAudio

`NAudio.dll`: NAudio **1.10.0**, net35, лицензия Microsoft Public License (Ms-PL). Copyright © Mark Heath и участники проекта.

Текст лицензии: `licenses/NAudio-Ms-PL.txt`. Исходники: https://github.com/naudio/NAudio/tree/v1.10.0 . Оригинальный пакет: https://www.nuget.org/packages/NAudio/1.10.0 . В локальном переносимом архиве исходники также находятся в `third-party-source/NAudio-1.10.0.zip`.

## ReplayLite

Исходники приложения включены в `source/`; пользователь может изменять и пересобирать их для своих задач. Репозиторий содержит исходники и сведения о зависимостях; двоичные файлы FFmpeg и NAudio скачиваются скриптом `source/setup-deps.ps1` из указанных выше источников с проверкой SHA256.
