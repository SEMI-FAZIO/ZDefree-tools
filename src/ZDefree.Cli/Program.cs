using ZDefree.Cli;
using ZDefree.Core.Bootstrap;
using ZDefree.Core.Compilation;
using ZDefree.Core.Conversion;
using ZDefree.Core.Serialization;

return CliApp.Run(args);

namespace ZDefree.Cli
{
    internal static class CliApp
    {
        public static int Run(string[] args)
        {
            var argList = ExtractLangArg(args.ToList());

            if (argList.Count == 0)
            {
                PrintHelp();
                return 0;
            }

            string command = argList[0].ToLowerInvariant();
            var rest = argList.Skip(1).ToList();

            try
            {
                return command switch
                {
                    "compile"   => RunCompile(rest),
                    "bat2json"  => RunBat2Json(rest),
                    "bootstrap" => RunBootstrap(rest).GetAwaiter().GetResult(),
                    "version"   => RunVersion(),
                    "help" or "--help" or "-h" or "/?" => RunHelp(rest),
                    _ => UnknownCommand(command),
                };
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        private static List<string> ExtractLangArg(List<string> args)
        {
            for (int i = 0; i < args.Count - 1; i++)
            {
                if (args[i] == "--lang")
                {
                    Messages.SetLang(args[i + 1]);
                    args.RemoveAt(i + 1);
                    args.RemoveAt(i);
                    break;
                }
            }
            return args;
        }

        private static int RunCompile(List<string> args)
        {
            if (args.Count == 0)
            {
                Console.Error.WriteLine(Messages.CompileUsage);
                return 2;
            }

            string strategyPath = args[0];
            if (!File.Exists(strategyPath))
            {
                Console.Error.WriteLine(Messages.ErrFileNotFound(strategyPath));
                return 1;
            }

            var opt = new CompileOptions
            {
                GameTcpPorts = ReadOption(args, "--game-tcp"),
                GameUdpPorts = ReadOption(args, "--game-udp"),
                BinDir       = ReadOption(args, "--bin-dir") ?? "bin/",
                ListsDir     = ReadOption(args, "--lists-dir") ?? "lists/",
            };

            var strategy = StrategyLoader.LoadFromFile(strategyPath);
            string cli = new WinwsCompiler(opt).Compile(strategy);
            Console.WriteLine(cli);
            return 0;
        }

        private static int RunBat2Json(List<string> args)
        {
            if (args.Count == 0)
            {
                Console.Error.WriteLine(Messages.Bat2JsonUsage);
                return 2;
            }

            string inputBat = args[0];
            if (!File.Exists(inputBat))
            {
                Console.Error.WriteLine(Messages.ErrFileNotFound(inputBat));
                return 1;
            }

            string? outputPath = args.Count > 1 && !args[1].StartsWith("--") ? args[1] : null;
            string? idOverride   = ReadOption(args, "--id");
            string? nameOverride = ReadOption(args, "--name");

            var result = BatchToStrategy.Convert(inputBat, idOverride, nameOverride);
            string json = StrategyLoader.Serialize(result.Strategy);

            if (outputPath is null)
            {
                Console.WriteLine(json);
            }
            else
            {
                File.WriteAllText(outputPath, json);
                Console.WriteLine(Messages.WroteFile(outputPath));
            }

            if (result.Warnings.Count > 0)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine(Messages.WarningsHeader(result.Warnings.Count));
                foreach (var w in result.Warnings)
                {
                    Console.Error.WriteLine($"  - {w}");
                }
            }

            return 0;
        }

        private static async Task<int> RunBootstrap(List<string> args)
        {
            string outDir = ReadOption(args, "--out") ?? ".";
            bool skipWinws = args.Contains("--skip-winws");
            bool skipWinDivert = args.Contains("--skip-windivert");
            bool skipPatterns = args.Contains("--skip-patterns");
            string? archStr = ReadOption(args, "--arch");

            var arch = archStr?.ToLowerInvariant() switch
            {
                "x64"   => WinwsArch.X64,
                "x86"   => WinwsArch.X86,
                "arm64" => WinwsArch.Arm64,
                null    => WinwsArch.Auto,
                _       => throw new ArgumentException($"Unknown --arch value: {archStr}. Use x64, x86, or arm64."),
            };

            var options = new BootstrapOptions
            {
                TargetDir = outDir,
                DownloadWinws = !skipWinws,
                DownloadWinDivert = !skipWinDivert,
                DownloadPatterns = !skipPatterns,
                Arch = arch,
                Progress = new Progress<BootstrapProgress>(p =>
                {
                    string line = Messages.BootstrapStage(p.Component, p.Stage);
                    if (p.PercentComplete.HasValue) line += $" {p.PercentComplete}%";
                    Console.Error.WriteLine(line);
                }),
            };

            Console.Error.WriteLine(Messages.BootstrapHeader(Path.GetFullPath(outDir)));

            using var runner = new BootstrapRunner();
            var result = await runner.RunAsync(options);

            if (result.Warnings.Count > 0)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine(Messages.WarningsHeader(result.Warnings.Count));
                foreach (var w in result.Warnings)
                {
                    Console.Error.WriteLine($"  - {w}");
                }
            }

            Console.Error.WriteLine();
            Console.WriteLine(Messages.BootstrapDone(result.InstalledFiles.Count));
            return 0;
        }

        private static int RunVersion()
        {
            var v = typeof(CliApp).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            Console.WriteLine($"zdefree {v}");
            return 0;
        }

        private static int RunHelp(List<string> _)
        {
            PrintHelp();
            return 0;
        }

        private static int UnknownCommand(string cmd)
        {
            Console.Error.WriteLine(Messages.ErrUnknownCommand(cmd));
            return 2;
        }

        private static void PrintHelp()
        {
            Console.WriteLine(Messages.UsageHeader);
            Console.WriteLine();
            Console.WriteLine(Messages.UsageBody);
        }

        private static string? ReadOption(List<string> args, string name)
        {
            for (int i = 0; i < args.Count - 1; i++)
            {
                if (args[i] == name)
                {
                    return args[i + 1];
                }
            }
            return null;
        }
    }
}
