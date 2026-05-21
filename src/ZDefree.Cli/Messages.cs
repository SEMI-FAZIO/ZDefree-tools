namespace ZDefree.Cli;

internal static class Messages
{
    public static string Lang { get; private set; } = DetectLang();

    private static string DetectLang()
    {
        string? env = Environment.GetEnvironmentVariable("ZDEFREE_LANG");
        if (!string.IsNullOrEmpty(env)) return env.ToLowerInvariant();

        string ui = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return ui.Equals("ru", StringComparison.OrdinalIgnoreCase) ? "ru" : "en";
    }

    public static void SetLang(string lang) => Lang = lang.ToLowerInvariant();

    public static string Get(string en, string ru) => Lang == "ru" ? ru : en;

    public static string UsageHeader => Get(
        "zdefree — Russian DPI-bypass distribution tool",
        "zdefree — инструмент дистрибутива обхода DPI РКН");

    public static string UsageBody => Get("""
Usage: zdefree <command> [options]

Commands:
  compile <strategy.json>            Compile a strategy to a winws.exe command line
  bat2json <input.bat> [output.json] Convert a Flowseal .bat into a ZDefree strategy
  bootstrap [--out <dir>] [--include-nfqws]
                                     Download winws.exe + WinDivert + lists; optionally nfqws (Linux)
  lists [--out <dir>]                Download domain/IP lists from 1andrevich/Re-filter-lists
  index [--strategies-dir <dir>]     (Re)generate strategies/INDEX.json catalog
  probe [--target <host>]...         Measure HTTPS+ping latency to target hosts
  isp [--json]                       Detect external IP + ASN/ISP via ipinfo.io
  pick [--isp auto|<tag>] [--dry-run | --live]
                                     Rank strategies (ISP-matched first; live = spawn winws + probe each)
  watch [--strategies-dir <dir>]     Watch strategy files for changes and stream events
  module <list|add|remove|...>       Manage strategy-pack modules
  version                            Print version
  help [command]                     Show help

Global options:
  --lang en|ru                       Force interface language (default: from OS)

Examples:
  zdefree compile strategies/common/general.json
  zdefree bat2json flowseal/general.bat strategies/common/general.json
""", """
Использование: zdefree <команда> [параметры]

Команды:
  compile <strategy.json>            Скомпилировать стратегию в командную строку winws.exe
  bat2json <input.bat> [output.json] Конвертировать .bat Flowseal в стратегию ZDefree
  bootstrap [--out <каталог>] [--include-nfqws]
                                     Скачать winws.exe + WinDivert + списки; опционально nfqws (Linux)
  lists [--out <каталог>]            Скачать списки доменов/IP из 1andrevich/Re-filter-lists
  index [--strategies-dir <каталог>] (Пере)генерировать каталог strategies/INDEX.json
  probe [--target <хост>]...         Измерить HTTPS+ping латентность до хостов
  isp [--json]                       Определить внешний IP + ASN/ISP через ipinfo.io
  pick [--isp auto|<тег>] [--dry-run | --live]
                                     Ранжировать стратегии (ISP-совпадения сверху; live = запуск winws + probe)
  watch [--strategies-dir <каталог>] Следить за изменениями стратегий и стримить события
  module <list|add|remove|...>       Управление модулями (strategy-packs)
  version                            Показать версию
  help [команда]                     Показать справку

Глобальные параметры:
  --lang en|ru                       Принудительно задать язык интерфейса (по умолчанию — из ОС)

Примеры:
  zdefree compile strategies/common/general.json
  zdefree bat2json flowseal/general.bat strategies/common/general.json
""");

    public static string CompileUsage => Get(
        "Usage: zdefree compile <strategy.json> [--target winws|nfqws] [--game-tcp <ports>] [--game-udp <ports>] [--bin-dir <path>] [--lists-dir <path>] [--qnum N]",
        "Использование: zdefree compile <strategy.json> [--target winws|nfqws] [--game-tcp <порты>] [--game-udp <порты>] [--bin-dir <путь>] [--lists-dir <путь>] [--qnum N]");

    public static string Bat2JsonUsage => Get(
        "Usage: zdefree bat2json <input.bat> [output.json] [--id <id>] [--name <name>]",
        "Использование: zdefree bat2json <input.bat> [output.json] [--id <идентификатор>] [--name <имя>]");

    public static string BootstrapUsage => Get(
        "Usage: zdefree bootstrap [--out <dir>] [--skip-winws] [--skip-windivert] [--skip-patterns] [--arch x64|x86|arm64]",
        "Использование: zdefree bootstrap [--out <каталог>] [--skip-winws] [--skip-windivert] [--skip-patterns] [--arch x64|x86|arm64]");

    public static string BootstrapHeader(string dir) => Get(
        $"Bootstrapping ZDefree distribution into {dir}",
        $"Сборка дистрибутива ZDefree в {dir}");

    public static string BootstrapDone(int filesInstalled) => Get(
        $"Done. Installed {filesInstalled} file(s).",
        $"Готово. Установлено файлов: {filesInstalled}.");

    public static string BootstrapStage(string component, string stage) => Get(
        $"[{component}] {stage}",
        $"[{component}] {stage}");

    public static string ErrFileNotFound(string path) => Get(
        $"Error: file not found — {path}",
        $"Ошибка: файл не найден — {path}");

    public static string ErrUnknownCommand(string cmd) => Get(
        $"Error: unknown command '{cmd}'. Run 'zdefree help' to see all commands.",
        $"Ошибка: неизвестная команда «{cmd}». Запустите «zdefree help» для списка команд.");

    public static string WroteFile(string path) => Get(
        $"Wrote {path}",
        $"Записан файл: {path}");

    public static string WarningsHeader(int n) => Get(
        $"{n} warning(s):",
        $"Предупреждений: {n}");

    public static string IndexUsage => Get(
        "Usage: zdefree index [--strategies-dir <path>] [--out <path>] [--check]",
        "Использование: zdefree index [--strategies-dir <путь>] [--out <путь>] [--check]");

    public static string IndexDone(int n, string path) => Get(
        $"Wrote {path} ({n} strategies)",
        $"Записан {path} (стратегий: {n})");

    public static string IndexCheckOk(int n) => Get(
        $"INDEX.json is up to date ({n} strategies).",
        $"INDEX.json актуален (стратегий: {n}).");

    public static string IndexCheckMismatch(string path) => Get(
        $"INDEX.json is stale at {path}. Regenerate with: zdefree index",
        $"INDEX.json устарел: {path}. Перегенерировать: zdefree index");

    public static string ListsUsage => Get(
        "Usage: zdefree lists [--out <dir>] [--pack <name1,name2,...>] [--validate-only]",
        "Использование: zdefree lists [--out <каталог>] [--pack <имя1,имя2,...>] [--validate-only]");

    public static string ListsDone(int packCount) => Get(
        $"Done. Installed {packCount} list pack(s).",
        $"Готово. Установлено пакетов списков: {packCount}.");

    public static string ModuleUsage => Get("""
Usage: zdefree module <subcommand>

Subcommands:
  list                                 Show all installed modules
  add <path>                           Add a local module directory (must contain module.json)
  remove <name>                        Delete the module dir
  trust <name>     / untrust <name>    Toggle trusted flag (untrusted modules are skipped by index)
  enable <name>    / disable <name>    Toggle enabled flag

Global options:
  --strategies-dir <dir>               Override strategies/ root (default: ./strategies)
""", """
Использование: zdefree module <субкоманда>

Субкоманды:
  list                                 Список установленных модулей
  add <путь>                           Добавить локальный модуль (нужен module.json)
  remove <имя>                         Удалить модуль
  trust <имя>      / untrust <имя>     Переключить флаг доверия (untrusted модули в index не попадают)
  enable <имя>     / disable <имя>     Переключить флаг включения

Глобальные параметры:
  --strategies-dir <каталог>           Переопределить корень strategies/ (по умолчанию ./strategies)
""");

    public static string ProbeUsage => Get(
        "Usage: zdefree probe [--target <host[:port]>]... [--timeout-ms N] [--no-ping] [--json]",
        "Использование: zdefree probe [--target <хост[:порт]>]... [--timeout-ms N] [--no-ping] [--json]");
}
