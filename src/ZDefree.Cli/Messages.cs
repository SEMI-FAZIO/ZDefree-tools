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
  bootstrap [--out <dir>]            Download winws.exe + WinDivert into a ZDefree folder
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
  bootstrap [--out <каталог>]        Скачать winws.exe + WinDivert в папку ZDefree
  version                            Показать версию
  help [команда]                     Показать справку

Глобальные параметры:
  --lang en|ru                       Принудительно задать язык интерфейса (по умолчанию — из ОС)

Примеры:
  zdefree compile strategies/common/general.json
  zdefree bat2json flowseal/general.bat strategies/common/general.json
""");

    public static string CompileUsage => Get(
        "Usage: zdefree compile <strategy.json> [--game-tcp <ports>] [--game-udp <ports>] [--bin-dir <path>] [--lists-dir <path>]",
        "Использование: zdefree compile <strategy.json> [--game-tcp <порты>] [--game-udp <порты>] [--bin-dir <путь>] [--lists-dir <путь>]");

    public static string Bat2JsonUsage => Get(
        "Usage: zdefree bat2json <input.bat> [output.json] [--id <id>] [--name <name>]",
        "Использование: zdefree bat2json <input.bat> [output.json] [--id <идентификатор>] [--name <имя>]");

    public static string BootstrapUsage => Get(
        "Usage: zdefree bootstrap [--out <dir>] [--skip-winws] [--skip-windivert] [--arch x64|x86|arm64]",
        "Использование: zdefree bootstrap [--out <каталог>] [--skip-winws] [--skip-windivert] [--arch x64|x86|arm64]");

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
}
