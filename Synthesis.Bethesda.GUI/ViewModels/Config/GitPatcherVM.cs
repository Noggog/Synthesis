using Synthesis.Bethesda.Execution.Settings;
using Noggog;
using Noggog.WPF;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using LibGit2Sharp;
using System.IO;
using System.Linq;
using DynamicData.Binding;
using DynamicData;
using static Synthesis.Bethesda.GUI.SolutionPatcherVM;
using Microsoft.WindowsAPICodePack.Dialogs;
using System.Threading.Tasks;
using System.Windows.Input;
using Synthesis.Bethesda.Execution.Patchers.Git;
using Synthesis.Bethesda.Execution.Patchers;

namespace Synthesis.Bethesda.GUI
{
    public class GitPatcherVM : PatcherVM
    {
        [Reactive]
        public string RemoteRepoPath { get; set; } = string.Empty;

        private readonly ObservableAsPropertyHelper<ConfigurationState> _InternalState;
        protected override ConfigurationState InternalState => _InternalState?.Value ?? ConfigurationState.Evaluating;

        public string ID { get; private set; } = string.Empty;

        public string LocalDriverRepoDirectory { get; }
        public string LocalRunnerRepoDirectory { get; }

        private readonly ObservableAsPropertyHelper<ErrorResponse> _RepoValidity;
        public ErrorResponse RepoValidity => _RepoValidity.Value;

        private readonly ObservableAsPropertyHelper<bool> _RepoClonesValid;
        public bool RepoClonesValid => _RepoClonesValid.Value;

        public IObservableCollection<string> AvailableProjects { get; }

        [Reactive]
        public string ProjectSubpath { get; set; } = string.Empty;

        public PathPickerVM SelectedProjectPath { get; } = new PathPickerVM()
        {
            ExistCheckOption = PathPickerVM.CheckOptions.On,
            PathType = PathPickerVM.PathTypeOptions.File,
        };

        [Reactive]
        public PatcherVersioningEnum PatcherVersioning { get; set; } = PatcherVersioningEnum.Branch;

        public IObservableCollection<string> AvailableTags { get; }

        [Reactive]
        public string TargetTag { get; set; } = string.Empty;

        [Reactive]
        public bool LatestTag { get; set; } = true;

        [Reactive]
        public string TargetCommit { get; set; } = string.Empty;

        [Reactive]
        public bool FollowDefaultBranch { get; set; } = true;

        [Reactive]
        public string TargetBranchName { get; set; } = string.Empty;

        private readonly ObservableAsPropertyHelper<RunnerRepoInfo?> _RunnableData;
        public RunnerRepoInfo? RunnableData => _RunnableData.Value;

        public ICommand OpenGitPageCommand { get; }

        public ICommand OpenGitPageToVersionCommand { get; }

        public ICommand NavigateToInternalFilesCommand { get; }

        [Reactive]
        public NugetVersioningEnum MutagenVersioning { get; set; } = NugetVersioningEnum.Latest;

        [Reactive]
        public string ManualMutagenVersion { get; set; } = string.Empty;

        [Reactive]
        public NugetVersioningEnum SynthesisVersioning { get; set; } = NugetVersioningEnum.Latest;

        [Reactive]
        public string ManualSynthesisVersion { get; set; } = string.Empty;

        private readonly ObservableAsPropertyHelper<(string? MatchVersion, string? SelectedVersion)> _UsedMutagenVersion;
        public (string? MatchVersion, string? SelectedVersion) UsedMutagenVersion => _UsedMutagenVersion.Value;

        private readonly ObservableAsPropertyHelper<(string? MatchVersion, string? SelectedVersion)> _UsedSynthesisVersion;
        public (string? MatchVersion, string? SelectedVersion) UsedSynthesisVersion => _UsedSynthesisVersion.Value;

        public GitPatcherVM(ProfileVM parent, GithubPatcherSettings? settings = null)
            : base(parent, settings)
        {
            SelectedProjectPath.Filters.Add(new CommonFileDialogFilter("Project", ".csproj"));

            CopyInSettings(settings);

            var localRepoDir = Path.Combine(Profile.ProfileDirectory, "Git", ID);
            LocalDriverRepoDirectory = Path.Combine(localRepoDir, "Driver");
            LocalRunnerRepoDirectory = GitPatcherRun.RunnerRepoDirectory(Profile.ID, ID);

            // Check to see if remote path points to a reachable git repository
            var remoteRepoPath = GetRepoPathValidity(this.WhenAnyValue(x => x.RemoteRepoPath))
                .Replay(1)
                .RefCount();
            _RepoValidity = remoteRepoPath
                .Select(r => r.RunnableState)
                .ToGuiProperty(this, nameof(RepoValidity));

            // Clone repository to a folder where driving information will be retreived from master.
            // This will be where we get available projects + tags, etc.
            var driverRepoInfo = remoteRepoPath
                .Throttle(TimeSpan.FromMilliseconds(100), RxApp.MainThreadScheduler)
                .ObserveOn(RxApp.TaskpoolScheduler)
                .SelectReplaceWithIntermediate(
                    new ConfigurationState<DriverRepoInfo>(default!)
                    {
                        IsHaltingError = false,
                        RunnableState = ErrorResponse.Fail("Cloning driver repository"),
                    },
                    async (path, cancel) =>
                    {
                        if (!path.IsHaltingError && path.RunnableState.Failed) return path.BubbleError<DriverRepoInfo>();
                        using var timing = Logger.Time("Cloning driver repository");
                        // Clone and/or double check the clone is correct
                        var state = await GitPatcherRun.CheckOrCloneRepo(path.ToGetResponse(), LocalDriverRepoDirectory, (x) => Logger.Information(x), cancel);
                        if (state.Failed) return new ConfigurationState<DriverRepoInfo>(default!, (ErrorResponse)state);
                        cancel.ThrowIfCancellationRequested();

                        // Grab all the interesting metadata
                        List<(int Index, string Name)> tags;
                        string masterBranch;
                        try
                        {
                            using var repo = new Repository(LocalDriverRepoDirectory);
                            var master = repo.Branches.Where(b => b.IsCurrentRepositoryHead).FirstOrDefault();
                            masterBranch = master.FriendlyName;
                            repo.Reset(ResetMode.Hard);
                            Commands.Checkout(repo, master);
                            Signature author = new Signature("please", "whymustidothis@gmail.com", DateTimeOffset.Now);
                            Commands.Pull(repo, author, null);
                            tags = repo.Tags.Select(tag => tag.FriendlyName).WithIndex().ToList();
                        }
                        catch (Exception ex)
                        {
                            return new ConfigurationState<DriverRepoInfo>(default!, ErrorResponse.Fail(ex));
                        }

                        // Try to locate a solution to drive from
                        var slnPath = GitPatcherRun.GetPathToSolution(LocalDriverRepoDirectory);
                        if (slnPath == null) return new ConfigurationState<DriverRepoInfo>(default!, ErrorResponse.Fail("Could not locate solution to run."));
                        var availableProjs = Utility.AvailableProjectSubpaths(slnPath).ToList();
                        return new ConfigurationState<DriverRepoInfo>(
                            new DriverRepoInfo(
                                slnPath: slnPath,
                                masterBranchName: masterBranch,
                                tags: tags,
                                availableProjects: availableProjs));
                    })
                .Replay(1)
                .RefCount();

            // Clone a second repository that we will check out the desired target commit to actually run
            var runnerRepoState = remoteRepoPath
                .Throttle(TimeSpan.FromMilliseconds(100), RxApp.MainThreadScheduler)
                .ObserveOn(RxApp.TaskpoolScheduler)
                .SelectReplaceWithIntermediate(
                    new ConfigurationState(ErrorResponse.Fail("Cloning runner repository"))
                    {
                        IsHaltingError = false
                    },
                    async (path, cancel) =>
                    {
                        if (path.RunnableState.Failed) return new ConfigurationState(path.RunnableState);
                        var log = Logger.ForContext("RemotePath", path.Item);
                        using var timing = log.Time("runner repo");
                        return (ErrorResponse)await GitPatcherRun.CheckOrCloneRepo(path.ToGetResponse(), LocalRunnerRepoDirectory, x => log.Information(x), cancel);
                    })
                .Replay(1)
                .RefCount();

            _RepoClonesValid = Observable.CombineLatest(
                    driverRepoInfo,
                    runnerRepoState,
                    (driver, runner) => driver.RunnableState.Succeeded && runner.RunnableState.Succeeded)
                .ToGuiProperty(this, nameof(RepoClonesValid));

            AvailableProjects = driverRepoInfo
                .Select(x => x.Item?.AvailableProjects ?? Enumerable.Empty<string>())
                .Select(x => x.AsObservableChangeSet<string>())
                .Switch()
                .ToObservableCollection(this);

            var tagInput = Observable.CombineLatest(
                this.WhenAnyValue(x => x.SelectedProjectPath.TargetPath),
                this.WhenAnyValue(x => x.AvailableProjects.Count),
                (targetPath, count) => (targetPath, count));
            AvailableTags = driverRepoInfo
                .Select(x => x.Item?.Tags ?? Enumerable.Empty<(int Index, string Name)>())
                .Select(x => x.AsObservableChangeSet())
                .Switch()
                .Filter(
                    tagInput.Select(x =>
                    {
                        if (x.count == 0) return new Func<(int Index, string Name), bool>(_ => false);
                        if (x.count == 1) return new Func<(int Index, string Name), bool>(_ => true);
                        if (!x.targetPath.EndsWith(".csproj")) return new Func<(int Index, string Name), bool>(_ => false);
                        var projName = Path.GetFileName(x.targetPath);
                        return new Func<(int Index, string Name), bool>(i => i.Name.StartsWith(projName, StringComparison.OrdinalIgnoreCase));
                    }))
                .Sort(SortExpressionComparer<(int Index, string Name)>.Descending(x => x.Index))
                .Transform(x => x.Name)
                .ToObservableCollection(this);

            var projPath = SolutionPatcherConfigLogic.ProjectPath(
                driverRepoInfo
                    .Select(x => x.Item?.SolutionPath ?? string.Empty),
                this.WhenAnyValue(x => x.ProjectSubpath));

            projPath
                .Subscribe(p => SelectedProjectPath.TargetPath = p)
                .DisposeWith(this);

            // Set latest checkboxes to drive user input
            driverRepoInfo.Select(x => x.RunnableState.Failed ? string.Empty : x.Item.MasterBranchName)
                .FilterSwitch(this.WhenAnyValue(x => x.FollowDefaultBranch))
                .Throttle(TimeSpan.FromMilliseconds(150), RxApp.MainThreadScheduler)
                .Subscribe(x => TargetBranchName = x)
                .DisposeWith(this);
            driverRepoInfo.Select(x => x.RunnableState.Failed ? string.Empty : x.Item.Tags.OrderByDescending(x => x.Index).Select(x => x.Name).FirstOrDefault())
                .FilterSwitch(this.WhenAnyValue(x => x.LatestTag))
                .Throttle(TimeSpan.FromMilliseconds(150), RxApp.MainThreadScheduler)
                .Subscribe(x => TargetTag = x)
                .DisposeWith(this);

            // Get the selected versioning preferences
            var patcherVersioning = Observable.CombineLatest(
                this.WhenAnyValue(x => x.PatcherVersioning),
                this.WhenAnyValue(x => x.TargetTag),
                this.WhenAnyValue(x => x.TargetCommit),
                this.WhenAnyValue(x => x.TargetBranchName),
                (versioning, tag, commit, branch) => GitPatcherVersioning.Factory(versioning, tag, commit, branch));

            var nugetVersioning = Observable.CombineLatest(
                    this.WhenAnyValue(x => x.MutagenVersioning),
                    this.WhenAnyValue(x => x.ManualMutagenVersion),
                    parent.Config.MainVM.NewestMutagenVersion,
                    this.WhenAnyValue(x => x.SynthesisVersioning),
                    this.WhenAnyValue(x => x.ManualSynthesisVersion),
                    parent.Config.MainVM.NewestSynthesisVersion,
                    (mutaVersioning, mutaManual, newestMuta, synthVersioning, synthManual, newestSynth) =>
                    {
                        return new SynthesisNugetVersioning(
                            new NugetVersioning(mutaVersioning, mutaManual, newestMuta),
                            new NugetVersioning(synthVersioning, synthManual, newestSynth));
                    });

            var libraryNugets = nugetVersioning
                .Select(nuget =>
                {
                    if (nuget.Mutagen.Versioning == NugetVersioningEnum.Latest && nuget.Mutagen.NewestVersion == null)
                    {
                        return GetResponse<NugetVersioningTarget>.Fail("Latest Mutagen version is desired, but latest version is not known.");
                    }
                    if (nuget.Synthesis.Versioning == NugetVersioningEnum.Latest && nuget.Synthesis.NewestVersion == null)
                    {
                        return GetResponse<NugetVersioningTarget>.Fail("Latest Synthesis version is desired, but latest version is not known.");
                    }
                    var muta = nuget.Mutagen.Versioning switch
                    {
                        NugetVersioningEnum.Latest => (nuget.Mutagen.NewestVersion, nuget.Mutagen.Versioning),
                        NugetVersioningEnum.Match => (null, nuget.Mutagen.Versioning),
                        NugetVersioningEnum.Manual => (nuget.Mutagen.ManualVersion, nuget.Mutagen.Versioning),
                        _ => throw new NotImplementedException(),
                    };
                    var synth = nuget.Synthesis.Versioning switch
                    {
                        NugetVersioningEnum.Latest => (nuget.Synthesis.NewestVersion, nuget.Synthesis.Versioning),
                        NugetVersioningEnum.Match => (null, nuget.Synthesis.Versioning),
                        NugetVersioningEnum.Manual => (nuget.Synthesis.ManualVersion, nuget.Synthesis.Versioning),
                        _ => throw new NotImplementedException(),
                    };
                    return GetResponse<NugetVersioningTarget>.Succeed(new NugetVersioningTarget(muta.Item1, muta.Versioning, synth.Item1, synth.Versioning));
                })
                .Replay(1)
                .RefCount();

            // Checkout desired patcher commit on the runner repository
            var checkoutInput = Observable.CombineLatest(
                    runnerRepoState,
                    SelectedProjectPath.PathState()
                        .Select(x => x.Succeeded ? x : GetResponse<string>.Fail("No patcher project selected.")),
                    patcherVersioning,
                    libraryNugets,
                    (runnerState, proj, patcherVersioning, libraryNugets) =>
                    (runnerState, proj, patcherVersioning, libraryNugets))
                .Replay(1)
                .RefCount();
            var runnableState = checkoutInput
                .Throttle(TimeSpan.FromMilliseconds(150), RxApp.MainThreadScheduler)
                .DistinctUntilChanged()
                .ObserveOn(RxApp.TaskpoolScheduler)
                .SelectReplaceWithIntermediate(
                    new ConfigurationState<RunnerRepoInfo>(default!)
                    {
                        RunnableState = ErrorResponse.Fail("Checking out the proper commit"),
                        IsHaltingError = false,
                    },
                    async (item, cancel) =>
                    {
                        async Task<ConfigurationState<RunnerRepoInfo>> Execute()
                        {
                            if (item.runnerState.RunnableState.Failed) return item.runnerState.BubbleError<RunnerRepoInfo>();
                            if (item.proj.Failed) return item.proj.BubbleFailure<RunnerRepoInfo>();
                            if (item.libraryNugets.Failed) return item.libraryNugets.BubbleFailure<RunnerRepoInfo>();

                            return await GitPatcherRun.CheckoutRunnerRepository(
                                proj: item.proj.Value,
                                localRepoDir: LocalRunnerRepoDirectory,
                                patcherVersioning: item.patcherVersioning,
                                nugetVersioning: item.libraryNugets.Value,
                                logger: (s) => Logger.Information(s),
                                cancel: cancel);
                        }
                        var ret = await Execute();
                        if (ret.RunnableState.Succeeded)
                        {
                            Logger.Information($"Finished checking out");
                        }
                        else
                        {
                            Logger.Error(ret.RunnableState, $"Failed checking out");
                        }
                        return ret;
                    })
                .Replay(1)
                .RefCount();

            _RunnableData = runnableState
                .Select(x => x.RunnableState.Succeeded ? x.Item : default(RunnerRepoInfo?))
                .ToGuiProperty(this, nameof(RunnableData));

            _UsedMutagenVersion = Observable.CombineLatest(
                    this.WhenAnyValue(x => x.RunnableData)
                        .Select(x => x?.ListedMutagenVersion),
                    libraryNugets.Select(x => x.Value?.MutagenVersion),
                    (matchVersion, selVersion) => (matchVersion, selVersion))
                .ToGuiProperty(this, nameof(UsedMutagenVersion));

            _UsedSynthesisVersion = Observable.CombineLatest(
                    this.WhenAnyValue(x => x.RunnableData)
                        .Select(x => x?.ListedSynthesisVersion),
                    libraryNugets.Select(x => x.Value?.SynthesisVersion),
                    (matchVersion, selVersion) => (matchVersion, selVersion))
                .ToGuiProperty(this, nameof(UsedSynthesisVersion));

            _InternalState = Observable.CombineLatest(
                    driverRepoInfo
                        .Select(x => x.ToUnit()),
                    runnerRepoState,
                    runnableState
                        .Select(x => x.ToUnit()),
                    this.WhenAnyValue(x => x.Profile.Config.MainVM)
                        .Select(x => x.DotNetSdkInstalled)
                        .Switch(),
                    (driver, runner, checkout, dotnet) =>
                    {
                        if (driver.IsHaltingError) return driver;
                        if (runner.IsHaltingError) return runner;
                        if (dotnet == null) return new ConfigurationState(ErrorResponse.Fail("No dotnet SDK installed"));
                        return checkout;
                    })
                .ToGuiProperty<ConfigurationState>(this, nameof(InternalState), ConfigurationState.Evaluating);

            OpenGitPageCommand = ReactiveCommand.Create(
                canExecute: this.WhenAnyValue(x => x.RepoValidity)
                    .Select(x => x.Succeeded),
                execute: () => Utility.NavigateToPath(RemoteRepoPath));

            OpenGitPageToVersionCommand = ReactiveCommand.Create(
                canExecute: this.WhenAnyValue(x => x.RunnableData)
                    .Select(x => x != null),
                execute: () =>
                {
                    try
                    {
                        if (!RunnableData.TryGet(out var runnable)) return;
                        if (runnable.Target == null)
                        {
                            Utility.NavigateToPath(RemoteRepoPath);
                        }
                        else
                        {
                            Utility.NavigateToPath(Path.Combine(RemoteRepoPath, "tree", runnable.Target));
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Error opening Git webpage", ex);
                    }
                });

            NavigateToInternalFilesCommand = ReactiveCommand.Create(() => Utility.NavigateToPath(localRepoDir));
        }

        public override PatcherSettings Save()
        {
            var ret = new GithubPatcherSettings
            {
                RemoteRepoPath = this.RemoteRepoPath,
                ID = this.ID,
                SelectedProjectSubpath = this.ProjectSubpath,
                PatcherVersioning = this.PatcherVersioning,
                MutagenVersioning = this.MutagenVersioning,
                ManualMutagenVersion = this.ManualMutagenVersion,
                SynthesisVersioning = this.SynthesisVersioning,
                ManualSynthesisVersion = this.ManualSynthesisVersion,
                TargetTag = this.TargetTag,
                TargetCommit = this.TargetCommit,
                LatestTag = this.LatestTag,
                FollowDefaultBranch = this.FollowDefaultBranch,
                TargetBranch = this.TargetBranchName,
            };
            CopyOverSave(ret);
            return ret;
        }

        private void CopyInSettings(GithubPatcherSettings? settings)
        {
            if (settings == null)
            {
                this.ID = Guid.NewGuid().ToString();
                return;
            }
            this.RemoteRepoPath = settings.RemoteRepoPath;
            this.ID = string.IsNullOrWhiteSpace(settings.ID) ? Guid.NewGuid().ToString() : settings.ID;
            this.ProjectSubpath = settings.SelectedProjectSubpath;
            this.PatcherVersioning = settings.PatcherVersioning;
            this.MutagenVersioning = settings.MutagenVersioning;
            this.SynthesisVersioning = settings.SynthesisVersioning;
            this.ManualMutagenVersion = settings.ManualMutagenVersion;
            this.ManualSynthesisVersion = settings.ManualSynthesisVersion;
            this.TargetTag = settings.TargetTag;
            this.TargetCommit = settings.TargetCommit;
            this.FollowDefaultBranch = settings.FollowDefaultBranch;
            this.LatestTag = settings.LatestTag;
            this.TargetBranchName = settings.TargetBranch;
        }

        public override PatcherRunVM ToRunner(PatchersRunVM parent)
        {
            if (RunnableData == null)
            {
                throw new ArgumentNullException(nameof(RunnableData));
            }
            return new PatcherRunVM(
                parent,
                this,
                new SolutionPatcherRun(
                    name: Name,
                    pathToSln: RunnableData.SolutionPath,
                    pathToExtraDataBaseFolder: Execution.Constants.TypicalExtraData,
                    pathToProj: RunnableData.ProjPath));
        }

        public static IObservable<ConfigurationState<string>> GetRepoPathValidity(IObservable<string> repoPath)
        {
            return repoPath
                .DistinctUntilChanged()
                .Select(x => new ConfigurationState<string>(string.Empty)
                {
                    IsHaltingError = false,
                    RunnableState = ErrorResponse.Fail("Checking remote repository correctness.")
                })
                // But merge in the work of checking the repo on that same path to get the eventual result
                .Merge(repoPath
                    .DistinctUntilChanged()
                    .Debounce(TimeSpan.FromMilliseconds(300), RxApp.MainThreadScheduler)
                    .ObserveOn(RxApp.TaskpoolScheduler)
                    .Select(p =>
                    {
                        try
                        {
                            if (Repository.ListRemoteReferences(p).Any()) return new ConfigurationState<string>(p);
                        }
                        catch (Exception)
                        {
                        }
                        return new ConfigurationState<string>(string.Empty, ErrorResponse.Fail("Path does not point to a valid repository."));
                    }));
        }

        public override void Delete()
        {
            base.Delete();
            try
            {
                var dir = new DirectoryInfo(this.LocalDriverRepoDirectory);
                dir.DeleteEntireFolder();
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, $"Failure deleting git repo: {this.LocalDriverRepoDirectory}");
            }
            try
            {
                var dir = new DirectoryInfo(this.LocalRunnerRepoDirectory);
                dir.DeleteEntireFolder();
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, $"Failure deleting git repo: {this.LocalRunnerRepoDirectory}");
            }
        }

        public override string GetDefaultName()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(RemoteRepoPath)) return "Mutagen Git Patcher";
                var span = RemoteRepoPath.AsSpan();
                var slashIndex = span.LastIndexOf('/');
                if (slashIndex != -1)
                {
                    span = span.Slice(slashIndex + 1);
                }
                return span.ToString();
            }
            catch (Exception)
            {
                return "Mutagen Git Patcher";
            }
        }
    }
}
