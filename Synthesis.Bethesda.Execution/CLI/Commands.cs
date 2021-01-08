using CommandLine;
using Mutagen.Bethesda;
using Newtonsoft.Json;
using Noggog;
using Noggog.Utility;
using Synthesis.Bethesda.Execution.Patchers;
using Synthesis.Bethesda.Execution.Patchers.Git;
using Synthesis.Bethesda.Execution.Reporters;
using Synthesis.Bethesda.Execution.Settings;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Synthesis.Bethesda.Execution.CLI
{
    /// <summary>
    /// Class that runs the patcher pipeline headless without a GUI
    /// </summary>
    public static class Commands
    {
        public static async Task Run(RunPatcherPipelineInstructions run, CancellationToken cancel, IRunReporter? reporter = null)
        {
            try
            {
                // Locate profile
                if (string.IsNullOrWhiteSpace(run.ProfileDefinitionPath))
                {
                    throw new ArgumentNullException("Profile", "Could not locate profile to run");
                }

                SynthesisProfile? profile;
                if (string.IsNullOrWhiteSpace(run.ProfileName))
                {
                    profile = JsonConvert.DeserializeObject<SynthesisProfile>(File.ReadAllText(run.ProfileDefinitionPath), Constants.JsonSettings)!;
                }
                else
                {
                    var settings = JsonConvert.DeserializeObject<PipelineSettings>(File.ReadAllText(run.ProfileDefinitionPath), Constants.JsonSettings)!;
                    profile = settings.Profiles.FirstOrDefault(profile =>
                    {
                        if (run.ProfileName.Equals(profile.Nickname)) return true;
                        if (run.ProfileName.Equals(profile.ID)) return true;
                        return false;
                    });
                }

                if (string.IsNullOrWhiteSpace(profile?.ID))
                {
                    throw new ArgumentException("File did not point to a valid profile");
                }

                if (profile.TargetRelease != run.GameRelease)
                {
                    throw new ArgumentException($"Target game release did not match profile's: {run.GameRelease} != {profile.TargetRelease}");
                }

                if (run.LoadOrderFilePath.IsNullOrWhitespace())
                {
                    run.LoadOrderFilePath = PluginListings.GetListingsPath(run.GameRelease);
                }

                reporter?.Write(default, "Patchers to run:");
                var patchers = profile.Patchers
                    .Where(p => p.On)
                    .Select<PatcherSettings, IPatcherRun>(patcherSettings =>
                    {
                        if (reporter != null)
                        {
                            patcherSettings.Print(reporter);
                        }
                        return patcherSettings switch
                        {
                            CodeSnippetPatcherSettings snippet => new CodeSnippetPatcherRun(snippet),
                            CliPatcherSettings cli => new CliPatcherRun(
                                cli.Nickname,
                                cli.PathToExecutable,
                                pathToExtra: null),
                            SolutionPatcherSettings sln => new SolutionPatcherRun(
                                name: sln.Nickname,
                                pathToSln: sln.SolutionPath,
                                pathToExtraDataBaseFolder: run.ExtraDataFolder ?? Constants.TypicalExtraData,
                                pathToProj: Path.Combine(Path.GetDirectoryName(sln.SolutionPath)!, sln.ProjectSubpath)),
                            GithubPatcherSettings git => new GitPatcherRun(
                                settings: git,
                                localDir: GitPatcherRun.RunnerRepoDirectory(profile.ID, git.ID)),
                            _ => throw new NotImplementedException(),
                        };
                    })
                    .ToList();

                await Runner.Run(
                    workingDirectory: Constants.ProfileWorkingDirectory(profile.ID),
                    outputPath: run.OutputPath,
                    dataFolder: run.DataFolderPath,
                    loadOrder: LoadOrder.GetListings(run.GameRelease, dataPath: run.DataFolderPath),
                    release: run.GameRelease,
                    patchers: patchers,
                    sourcePath: run.SourcePath == null ? default : ModPath.FromPath(run.SourcePath),
                    reporter: reporter,
                    cancel: cancel);
            }
            catch (Exception ex)
            when (reporter != null)
            {
                reporter.ReportOverallProblem(ex);
            }
        }

        public static async Task<SettingsTarget> GetSettingsStyle(
            string path,
            bool directExe,
            CancellationToken cancel)
        {
            using var proc = ProcessWrapper.Create(
                GetStart(path, directExe, new Synthesis.Bethesda.SettingsQuery()),
                cancel: cancel,
                hookOntoOutput: true);

            string? firstLine = null;
            bool tooMuch = false;
            using var outputSub = proc.Output
                .Subscribe(s =>
                {
                    if (firstLine == null)
                    {
                        firstLine = s;
                    }
                    else
                    {
                        tooMuch = true;
                    }
                });

            switch ((Codes)await proc.Run())
            {
                case Codes.OpensForSettings:
                    return new SettingsTarget(SettingsStyle.Open, null);
                case Codes.AutogeneratedSettingsClass:
                    if (firstLine == null || tooMuch)
                    {
                        throw new ArgumentException("Output not expected for autogenerated settings");
                    }
                    return new SettingsTarget(SettingsStyle.SpecifiedClass, firstLine);
                default:
                    return new SettingsTarget(SettingsStyle.None, null);
            }
        }

        public static async Task<int> OpenForSettings(
            string path,
            bool directExe,
            Rectangle rect,
            CancellationToken cancel)
        {
            using var proc = ProcessWrapper.Create(
                GetStart(path, directExe, new Synthesis.Bethesda.OpenForSettings()
                {
                    Left = rect.Left,
                    Top = rect.Top,
                    Height = rect.Height,
                    Width = rect.Width
                }),
                cancel: cancel,
                hookOntoOutput: false);

            return await proc.Run();
        }

        public static async Task<ErrorResponse> CheckRunnability(
            string path,
            bool directExe,
            GameRelease release,
            string dataFolder,
            IEnumerable<LoadOrderListing> loadOrder,
            CancellationToken cancel)
        {
            using var loadOrderFile = new TempFile(
                Path.Combine(Synthesis.Bethesda.Execution.Constants.WorkingDirectory, "RunnabilityChecks", Path.GetRandomFileName()));

            LoadOrder.Write(
                loadOrderFile.File.Path,
                release,
                loadOrder,
                removeImplicitMods: true);

            return await CheckRunnability(
                path,
                directExe: directExe,
                release: release,
                dataFolder: dataFolder,
                loadOrderPath: loadOrderFile.File.Path,
                cancel: cancel);
        }

        public static async Task<ErrorResponse> CheckRunnability(
            string path,
            bool directExe,
            GameRelease release,
            string dataFolder,
            string loadOrderPath,
            CancellationToken cancel)
        {
            var checkState = new Synthesis.Bethesda.CheckRunnability()
            {
                DataFolderPath = dataFolder,
                GameRelease = release,
                LoadOrderFilePath = loadOrderPath
            };

            using var proc = ProcessWrapper.Create(
                GetStart(path, directExe, checkState),
                cancel: cancel);

            var results = new List<string>();
            void AddResult(string s)
            {
                if (results.Count < 100)
                {
                    results.Add(s);
                }
            }
            using var ouputSub = proc.Output.Subscribe(AddResult);
            using var errSub = proc.Error.Subscribe(AddResult);

            var result = await proc.Run();

            if (result == (int)Codes.NotRunnable)
            {
                return ErrorResponse.Fail(string.Join(Environment.NewLine, results));
            }

            // Other error codes are likely the target app just not handling runnability checks, so return as runnable unless
            // explicity told otherwise with the above error code
            return ErrorResponse.Success;
        }

        private static ProcessStartInfo GetStart(string path, bool directExe, object args)
        {
            if (directExe)
            {
                return new ProcessStartInfo(path, Parser.Default.FormatCommandLine(args));
            }
            else
            {
                return new ProcessStartInfo("dotnet", $"run --project \"{path}\" --runtime win-x64 --no-build {Parser.Default.FormatCommandLine(args)}");
            }
        }
    }
}
