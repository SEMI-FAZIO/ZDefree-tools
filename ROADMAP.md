# ZDefree Roadmap

Status of the two-repo project as of the latest commit. Use this when
picking up work on a new machine or after a long break.

[Русская версия](#русская-версия)

---

## Project layout

```
GUI/                                            (parent folder)
├── ZDefree/             ← spec   (data: manifest, strategies, schema, lists)
│   https://github.com/SEMI-FAZIO/ZDefree
├── ZDefree-tools/       ← code   (.NET 8: compiler, bat2json, bootstrap, CLI)
│   https://github.com/SEMI-FAZIO/ZDefree-tools
└── ZapretGUI/           ← existing WPF GUI (not yet ZDefree-aware)
    https://github.com/SEMI-FAZIO/ZapretGUI
```

## Done

| Phase | What | Repo | Done |
|---|---|---|---|
| 0 | Research: bol-van/zapret-win-bundle, WinDivert licensing, lists, Flowseal convertibility | — | ✅ |
| 1 | ZDefree spec: LICENSE, THIRD_PARTY, README (EN/RU), manifest.json, strategies/schema.json, common/general.json | ZDefree | ✅ |
| C | `WinwsCompiler` — Strategy → winws CLI, with `{game_tcp}`/`{game_udp}` template substitution + custom bin/lists dir | ZDefree-tools | ✅ |
| A | `BatchTokenizer` + `BatchToStrategy` + `zdefree bat2json` CLI — Flowseal `.bat` → ZDefree JSON | ZDefree-tools | ✅ |
| B | `BootstrapRunner` + `zdefree bootstrap` — downloads winws.exe (bol-van) + WinDivert (basil00) + fake-pattern .bin files (Flowseal) | ZDefree-tools | ✅ |
| D | GitHub Actions CI on ZDefree-tools — restore/build/test on every push | ZDefree-tools | ✅ |

**69 unit tests covering** loader, compiler, batch tokenizer, batch→strategy
mapping, bootstrap helpers (allowlist, SHA-256, zip-slip protection, file
filters, arch detection). End-to-end roundtrip test confirms that
`bat2json general.bat → JSON → WinwsCompiler` produces byte-identical
output to compiling the handwritten `general.json`.

## Next up (pick any order)

### A. Mass-convert all Flowseal strategies (10–20 min)
Run `zdefree bat2json` over all ~15-20 `.bat` files in
`Flowseal/zapret-discord-youtube`. Should give us a full
`ZDefree/strategies/common/*.json` catalog. Watch for warnings
about unknown flags falling into `advanced.raw_args` — those need
to be added to the schema and the mapper switch.

```pwsh
# example
gh api repos/Flowseal/zapret-discord-youtube/contents/ --jq '.[] | select(.name | endswith(".bat")) | .download_url'
# download each, run: zdefree bat2json <bat> ZDefree/strategies/common/<name>.json
```

### B. Lists pipeline (1–2 hours)
Extend bootstrap to populate `ZDefree/lists/bundled/` from
`1andrevich/Re-filter-lists` (MIT). Each strategy `hostlist: "discord"`
should resolve to a real `lists/bundled/list-discord.txt`. Without
this, strategies have nothing to filter.

Consider:
- Per-app packs (discord, youtube, meta, cloudflare, ...)
- ipset variant (resolved IPs)
- Pull schedule / cache invalidation

### C. ZapretGUI integration (~1 day)
Teach `ZapretController.IsZapretRoot` to recognize a `manifest.json`
in the target folder. If present, the GUI runs in "native ZDefree
mode": loads JSON strategies via `ZDefree.Core` (reference the
library as a NuGet local project or just copy), bypasses
`BatchParser`. If absent, it stays in legacy Flowseal mode. One GUI,
two distributions, clean migration story.

### Other follow-ups, not blocking

- Single-file `dotnet publish` job in CI that uploads `zdefree.exe`
  to GitHub Releases on every tag (so users on machines without .NET
  SDK can just download)
- JSON Schema validation in `StrategyLoader` (today we do hand-rolled
  required-field checks; could swap in NJsonSchema for full coverage)
- More test fixtures (alt.bat, fake-tls.bat, simple-fake.bat) so
  regressions surface across the full strategy space
- `bin/VERSION` upgrade — record bol-van winws commit SHA properly
  (today only WinDivert tag fills in)

## Working from a fresh clone

```pwsh
# 1. Install prerequisites
#    Git: https://git-scm.com/download/win
#    .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0

# 2. Clone both repos side-by-side
mkdir GUI
cd GUI
git clone https://github.com/SEMI-FAZIO/ZDefree.git
git clone https://github.com/SEMI-FAZIO/ZDefree-tools.git
git clone https://github.com/SEMI-FAZIO/ZapretGUI.git   # optional, for GUI work

# 3. Verify tools build and tests pass
cd ZDefree-tools
dotnet restore
dotnet test          # expected: Passed: 69, Failed: 0

# 4. Smoke-test bootstrap (downloads ~20 MB)
dotnet run --project src/ZDefree.Cli -- bootstrap --out C:\test-zdefree

# 5. Smoke-test compile (uses absolute paths)
dotnet run --project src/ZDefree.Cli -- compile `
  ..\ZDefree\strategies\common\general.json `
  --game-tcp 1024-65535 --game-udp 1024-65535 `
  --bin-dir C:\test-zdefree\bin\ --lists-dir C:\test-zdefree\lists\
```

If anything is off, the CI on `main` of ZDefree-tools tells you whether
the issue is your environment or a real regression.

---

## Русская версия

Статус двух-репо проекта на момент последнего коммита. Этот файл —
точка возврата, если работа возобновляется с другой машины или после
перерыва.

### Раскладка проекта

```
GUI/                                            (родительская папка)
├── ZDefree/             ← спецификация (данные: manifest, стратегии, схема, списки)
│   https://github.com/SEMI-FAZIO/ZDefree
├── ZDefree-tools/       ← код (.NET 8: компилятор, bat2json, bootstrap, CLI)
│   https://github.com/SEMI-FAZIO/ZDefree-tools
└── ZapretGUI/           ← существующий WPF GUI (ZDefree пока не распознаёт)
    https://github.com/SEMI-FAZIO/ZapretGUI
```

### Сделано

См. таблицу выше — фазы 0, 1, A, B, C, D закрыты. **69 юнит-тестов** на
компиляторе, конвертере, bootstrap-хелперах. Сквозной roundtrip-тест:
`.bat → JSON → CLI` бит-в-бит совпадает с компиляцией ручной `general.json`.

### Что дальше

- **A.** Массовая конвертация всех `.bat` Flowseal (10–20 мин)
- **B.** Pipeline для списков доменов (1–2 часа)
- **C.** Интеграция с ZapretGUI (~день)
- Прочее: single-file публикация в CI/Releases, JSON Schema валидация,
  расширение тест-фикстур, корректный winws commit SHA в `bin/VERSION`

### Старт с чистого клона

См. английскую секцию — команды одинаковы.
Главное: после клона `dotnet test` должен выдать **Passed: 69, Failed: 0**.
Если нет — проверь, что стоит .NET 8 SDK (не Runtime), и сравни с CI
на ветке `main` ZDefree-tools.
