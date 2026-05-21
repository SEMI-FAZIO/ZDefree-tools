using System.Text.Json;
using ZDefree.Cli;
using ZDefree.Core.Bootstrap;
using ZDefree.Core.Compilation;
using ZDefree.Core.Conversion;
using ZDefree.Core.Probing;
using ZDefree.Core.Serialization;
using ZDefree.Core.Watching;

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
                    "index"     => RunIndex(rest),
                    "lists"     => RunLists(rest).GetAwaiter().GetResult(),
                    "probe"     => RunProbe(rest).GetAwaiter().GetResult(),
                    "isp"       => RunIsp(rest).GetAwaiter().GetResult(),
                    "pick"      => RunPick(rest).GetAwaiter().GetResult(),
                    "watch"     => RunWatch(rest).GetAwaiter().GetResult(),
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

            string target = (ReadOption(args, "--target") ?? "winws").ToLowerInvariant();
            var strategy  = StrategyLoader.LoadFromFile(strategyPath);

            string cli;
            switch (target)
            {
                case "winws":
                    cli = new WinwsCompiler(new CompileOptions
                    {
                        GameTcpPorts = ReadOption(args, "--game-tcp"),
                        GameUdpPorts = ReadOption(args, "--game-udp"),
                        BinDir       = ReadOption(args, "--bin-dir") ?? "bin/",
                        ListsDir     = ReadOption(args, "--lists-dir") ?? "lists/",
                    }).Compile(strategy);
                    break;

                case "nfqws":
                    cli = new NfqwsCompiler(new NfqwsCompileOptions
                    {
                        GameTcpPorts = ReadOption(args, "--game-tcp"),
                        GameUdpPorts = ReadOption(args, "--game-udp"),
                        BinDir       = ReadOption(args, "--bin-dir") ?? "bin/",
                        ListsDir     = ReadOption(args, "--lists-dir") ?? "lists/",
                        QueueNum     = int.TryParse(ReadOption(args, "--qnum"), out var q) ? q : 200,
                    }).Compile(strategy);
                    break;

                default:
                    Console.Error.WriteLine($"Error: unknown --target '{target}'. Use 'winws' or 'nfqws'.");
                    return 2;
            }

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
            bool skipLists = args.Contains("--skip-lists");
            bool includeNfqws = args.Contains("--include-nfqws");
            string? archStr = ReadOption(args, "--arch");
            string? nfqwsArchStr = ReadOption(args, "--nfqws-arch");

            var arch = archStr?.ToLowerInvariant() switch
            {
                "x64"   => WinwsArch.X64,
                "x86"   => WinwsArch.X86,
                "arm64" => WinwsArch.Arm64,
                null    => WinwsArch.Auto,
                _       => throw new ArgumentException($"Unknown --arch value: {archStr}. Use x64, x86, or arm64."),
            };

            var nfqwsArch = nfqwsArchStr?.ToLowerInvariant() switch
            {
                "x86_64" or "x64" => NfqwsArch.LinuxX64,
                "x86"             => NfqwsArch.LinuxX86,
                "arm"             => NfqwsArch.LinuxArm,
                "arm64"           => NfqwsArch.LinuxArm64,
                null              => NfqwsArch.Auto,
                _                 => throw new ArgumentException($"Unknown --nfqws-arch value: {nfqwsArchStr}. Use x86_64, x86, arm, or arm64."),
            };

            var options = new BootstrapOptions
            {
                TargetDir = outDir,
                DownloadWinws = !skipWinws,
                DownloadWinDivert = !skipWinDivert,
                DownloadPatterns = !skipPatterns,
                DownloadLists = !skipLists,
                DownloadNfqws = includeNfqws,
                NfqwsArchOverride = nfqwsArch,
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

        private static async Task<int> RunWatch(List<string> args)
        {
            string strategiesDir = ReadOption(args, "--strategies-dir") ?? "strategies";
            int debounceMs = int.TryParse(ReadOption(args, "--debounce-ms"), out var d) ? d : 250;
            bool json = args.Contains("--json");

            if (!Directory.Exists(strategiesDir))
            {
                Console.Error.WriteLine(Messages.ErrFileNotFound(strategiesDir));
                return 1;
            }

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, ev) =>
            {
                ev.Cancel = true;
                cts.Cancel();
            };

            using var watcher = new StrategyWatcher(strategiesDir, TimeSpan.FromMilliseconds(debounceMs));
            watcher.Changed += (_, e) =>
            {
                if (json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(e));
                    Console.Out.Flush();
                }
                else
                {
                    string ts = e.At.ToLocalTime().ToString("HH:mm:ss.fff");
                    string from = e.OldPath is null ? "" : $" (from {Path.GetFileName(e.OldPath)})";
                    Console.WriteLine($"[{ts}] {e.Kind,-8} {Path.GetFileName(e.FilePath)}{from}");
                }
            };

            watcher.Start();
            Console.Error.WriteLine($"Watching {Path.GetFullPath(strategiesDir)} (debounce={debounceMs}ms). Ctrl-C to stop.");

            try
            {
                await Task.Delay(Timeout.Infinite, cts.Token);
            }
            catch (TaskCanceledException) { }

            Console.Error.WriteLine("Stopped.");
            return 0;
        }

        private static async Task<int> RunPick(List<string> args)
        {
            string strategiesDir = ReadOption(args, "--strategies-dir") ?? "strategies";
            string ispMode       = ReadOption(args, "--isp") ?? "none";
            bool live            = args.Contains("--live");
            bool dryRun          = args.Contains("--dry-run") || !live;
            bool json            = args.Contains("--json");

            if (!Directory.Exists(strategiesDir))
            {
                Console.Error.WriteLine(Messages.ErrFileNotFound(strategiesDir));
                return 1;
            }

            using var ispDet = new IspDetector();
            var picker = new StrategyPicker(ispDet);
            var pickOpts = new PickerOptions
            {
                StrategiesDir = strategiesDir,
                IspMode       = ispMode,
                DryRun        = dryRun,
            };

            if (live)
            {
                return await RunPickLive(picker, pickOpts, args, json);
            }

            var result = await picker.RunAsync(pickOpts);

            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                if (result.DetectedIsp is { } isp)
                {
                    Console.WriteLine($"Detected ISP: {isp.CompatTag ?? isp.Asn ?? "-"} ({isp.OrgName ?? "-"})");
                }
                else if (ispMode != "none")
                {
                    Console.WriteLine($"ISP filter: {ispMode}");
                }
                Console.WriteLine();
                Console.WriteLine($"{"RANK",-5} {"ID",-32} {"CATEGORY",-12} ISP-MATCH");
                int rank = 1;
                foreach (var c in result.Candidates)
                {
                    string mark = c.IspMatched ? $"yes ({c.MatchedIspTag})" : "-";
                    Console.WriteLine($"{rank,-5} {c.Id,-32} {c.Category ?? "-",-12} {mark}");
                    rank++;
                }
            }

            return 0;
        }

        private static async Task<int> RunPickLive(StrategyPicker picker, PickerOptions pickOpts, List<string> args, bool json)
        {
            string? winwsPath = ReadOption(args, "--winws-path");
            string  binDir    = ReadOption(args, "--bin-dir")   ?? "bin/";
            string  listsDir  = ReadOption(args, "--lists-dir") ?? "lists/";
            string? gameTcp   = ReadOption(args, "--game-tcp");
            string? gameUdp   = ReadOption(args, "--game-udp");

            int stabilizationMs = int.TryParse(ReadOption(args, "--stabilization-ms"), out var sm) ? sm : 2000;
            int perStrategyMs   = int.TryParse(ReadOption(args, "--per-strategy-timeout-ms"), out var pm) ? pm : 15000;

            // Default winws path: <bin-dir>/winws.exe.
            winwsPath ??= Path.Combine(binDir, "winws.exe");

            if (!File.Exists(winwsPath))
            {
                Console.Error.WriteLine($"winws.exe not found at: {winwsPath}");
                Console.Error.WriteLine("Live mode requires a bootstrapped distribution. Run `zdefree bootstrap --out <dir>` first,");
                Console.Error.WriteLine("or pass --winws-path explicitly.");
                return 1;
            }

            var live = new LivePickerSettings
            {
                WinwsExePath        = winwsPath,
                BinDir              = binDir,
                ListsDir            = listsDir,
                GameTcpPorts        = gameTcp,
                GameUdpPorts        = gameUdp,
                StabilizationWait   = TimeSpan.FromMilliseconds(stabilizationMs),
                PerStrategyTimeout  = TimeSpan.FromMilliseconds(perStrategyMs),
            };

            using var probe = new ConnectionProbe();
            var result = await picker.RunLiveAsync(
                pickOpts,
                probe,
                () => new WinwsProcess(),
                live);

            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                if (result.DetectedIsp is { } isp)
                {
                    Console.WriteLine($"Detected ISP: {isp.CompatTag ?? isp.Asn ?? "-"} ({isp.OrgName ?? "-"})");
                }
                Console.WriteLine();
                Console.WriteLine($"{"RANK",-5} {"ID",-32} {"SCORE",-7} ERROR");
                int rank = 1;
                foreach (var c in result.Ranked)
                {
                    string err = c.Error ?? "";
                    Console.WriteLine($"{rank,-5} {c.Base.Id,-32} {c.Score,7:F1} {err}");
                    rank++;
                }
            }

            return 0;
        }

        private static async Task<int> RunIsp(List<string> args)
        {
            bool json = args.Contains("--json");
            string? endpoint = ReadOption(args, "--endpoint");

            using var det = new IspDetector(endpoint: endpoint);
            try
            {
                var info = await det.DetectAsync();
                if (json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
                }
                else
                {
                    Console.WriteLine($"IP        : {info.Ip}");
                    Console.WriteLine($"Country   : {info.Country ?? "-"}");
                    Console.WriteLine($"ASN       : {info.Asn ?? "-"}");
                    Console.WriteLine($"Org       : {info.OrgName ?? "-"}");
                    Console.WriteLine($"Compat tag: {info.CompatTag ?? "-"}");
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ISP detection failed: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        private static async Task<int> RunProbe(List<string> args)
        {
            // Collect repeated --target values.
            var targetSpecs = new List<string>();
            for (int i = 0; i < args.Count - 1; i++)
            {
                if (args[i] == "--target")
                {
                    targetSpecs.Add(args[i + 1]);
                }
            }

            IReadOnlyList<ProbeTarget> targets = targetSpecs.Count > 0
                ? targetSpecs.Select(ProbeTarget.Parse).ToList()
                : ConnectionProbe.DefaultTargets;

            int timeoutMs = int.TryParse(ReadOption(args, "--timeout-ms"), out var t) ? t : 3000;
            bool skipPing = args.Contains("--no-ping") || args.Contains("--skip-ping");
            bool json     = args.Contains("--json");

            var options = new ProbeOptions
            {
                Targets   = targets,
                TimeoutMs = timeoutMs,
                SkipPing  = skipPing,
            };

            using var probe = new ConnectionProbe();
            var results = await probe.RunAsync(options);

            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                Console.WriteLine($"{"TARGET",-30} {"HTTPS",-12} {"PING",-12} SCORE  NOTE");
                foreach (var r in results)
                {
                    string https = r.HttpsOk ? $"{r.HttpsMs:F0} ms"      : "FAIL";
                    string ping  = r.PingOk  ? $"{r.PingMs:F0} ms"       : (skipPing ? "-" : "FAIL");
                    Console.WriteLine($"{r.Target,-30} {https,-12} {ping,-12} {r.Score,5:F1}  {r.Error ?? ""}");
                }
            }

            return results.All(r => !r.HttpsOk) ? 1 : 0;
        }

        private static async Task<int> RunLists(List<string> args)
        {
            string outDir = ReadOption(args, "--out") ?? ".";
            bool validateOnly = args.Contains("--validate-only");
            string? packArg = ReadOption(args, "--pack");
            var packs = packArg?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

            var options = new ListsOptions
            {
                TargetDir   = outDir,
                Packs       = packs,
                ValidateOnly = validateOnly,
                Progress    = new Progress<BootstrapProgress>(p =>
                {
                    string line = Messages.BootstrapStage(p.Component, p.Stage);
                    if (p.PercentComplete.HasValue) line += $" {p.PercentComplete}%";
                    Console.Error.WriteLine(line);
                }),
            };

            using var gh = new GitHubClient();
            var installer = new ListsInstaller(gh);
            var result = await installer.InstallAsync(options);

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
            Console.WriteLine(Messages.ListsDone(result.Packs.Count));
            return 0;
        }

        private static int RunIndex(List<string> args)
        {
            string strategiesDir = ReadOption(args, "--strategies-dir") ?? "strategies";
            string outPath       = ReadOption(args, "--out") ?? Path.Combine(strategiesDir, "INDEX.json");
            bool check           = args.Contains("--check");

            if (!Directory.Exists(strategiesDir))
            {
                Console.Error.WriteLine(Messages.ErrFileNotFound(strategiesDir));
                return 1;
            }

            string generator = "zdefree index v" + (typeof(CliApp).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");
            var built = StrategyIndexBuilder.Build(strategiesDir, generator);

            if (check)
            {
                if (!File.Exists(outPath))
                {
                    Console.Error.WriteLine(Messages.IndexCheckMismatch(outPath));
                    return 1;
                }
                var existing = StrategyIndexBuilder.Deserialize(File.ReadAllText(outPath));
                if (!StrategyIndexBuilder.Equivalent(built, existing))
                {
                    Console.Error.WriteLine(Messages.IndexCheckMismatch(outPath));
                    return 1;
                }
                Console.WriteLine(Messages.IndexCheckOk(built.Strategies.Count));
                return 0;
            }

            File.WriteAllText(outPath, StrategyIndexBuilder.Serialize(built));
            Console.WriteLine(Messages.IndexDone(built.Strategies.Count, outPath));
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
