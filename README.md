# ZDefree.Tools

[![CI](https://github.com/SEMI-FAZIO/ZDefree-tools/actions/workflows/ci.yml/badge.svg)](https://github.com/SEMI-FAZIO/ZDefree-tools/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

.NET 8 tooling that powers the [ZDefree](https://github.com/SEMI-FAZIO/ZDefree)
distribution.

[Русская версия](#русская-версия)

---

## What's here

- **`ZDefree.Core`** — library shared by every tool. Strategy models, JSON
  loader, and `WinwsCompiler` (compiles a `strategy.json` into a
  `winws.exe` command-line string).
- **`ZDefree.Cli`** (coming) — single CLI binary with subcommands:
  - `zdefree compile <strategy.json>` — print the resulting winws args
  - `zdefree bat2json <flowseal.bat>` — convert a Flowseal `.bat` into
    ZDefree-flavored JSON
  - `zdefree bootstrap [--out <dir>]` — download `winws.exe`,
    `WinDivert`, and default lists into the target ZDefree folder

## Requirements

- **.NET 8 SDK** — https://dotnet.microsoft.com/download/dotnet/8.0
- Windows / Linux / macOS (Bootstrap output is Windows-only — `winws.exe`
  is a Windows binary — but the tools themselves run cross-platform)

## Build & test

```pwsh
dotnet restore
dotnet build -c Release
dotnet test
```

## Repo layout

```
ZDefree-tools/
├─ src/
│   ├─ ZDefree.Core/        ← shared library
│   └─ ZDefree.Cli/         ← (planned) `zdefree` CLI binary
├─ tests/
│   └─ ZDefree.Core.Tests/  ← xUnit tests + fixtures
└─ ZDefree.Tools.sln
```

## Why a separate repo

ZDefree (the distribution) is pure data — manifest, strategies, lists,
JSON schema. It is consumed by frontends and stays small. ZDefree.Tools is
.NET code that generates and validates that data, depends on the SDK, and
benefits from its own CI / release cycle. Keeping them apart means end
users of ZDefree don't need .NET to use it.

## Related projects

- [**ZDefree**](https://github.com/SEMI-FAZIO/ZDefree) — the distribution
  (data + schema this tooling generates)
- [**ZapretGUI**](https://github.com/SEMI-FAZIO/ZapretGUI) — reference WPF
  frontend that consumes a ZDefree-shaped folder

---

## Русская версия

.NET 8 инструменты для дистрибутива
[ZDefree](https://github.com/SEMI-FAZIO/ZDefree).

### Что внутри

- **`ZDefree.Core`** — общая библиотека. Модели стратегии, JSON-загрузчик
  и `WinwsCompiler` (компилирует `strategy.json` в командную строку
  `winws.exe`).
- **`ZDefree.Cli`** (планируется) — один CLI с субкомандами:
  - `zdefree compile <strategy.json>` — печатает итоговые args для winws
  - `zdefree bat2json <flowseal.bat>` — конвертирует `.bat` Flowseal в JSON
  - `zdefree bootstrap [--out <dir>]` — скачивает `winws.exe`, `WinDivert`
    и дефолтные списки в указанную папку ZDefree

### Требования

- **.NET 8 SDK** — https://dotnet.microsoft.com/download/dotnet/8.0
- Windows / Linux / macOS

### Сборка и тесты

```pwsh
dotnet restore
dotnet build -c Release
dotnet test
```

### Почему отдельный репо

ZDefree (дистрибутив) — это чистые данные: manifest, стратегии, списки,
JSON-схема. Его потребляют фронтенды и он остаётся маленьким. ZDefree.Tools
— это .NET-код, который эти данные генерирует и валидирует, требует SDK
и живёт со своим CI/релизным циклом. Разделение значит, что конечным
пользователям ZDefree .NET не нужен.

### Связанные проекты

- [**ZDefree**](https://github.com/SEMI-FAZIO/ZDefree) — дистрибутив
- [**ZapretGUI**](https://github.com/SEMI-FAZIO/ZapretGUI) — референсный
  WPF-фронтенд
