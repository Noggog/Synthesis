using Synthesis.Bethesda.Execution.Patchers;
using Synthesis.Bethesda.Execution.Settings;
using Noggog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Synthesis.CLI;
using System.Linq;

namespace Synthesis.Bethesda.UnitTests
{
    public class CodeSnippetTests
    {
        [Fact]
        public async Task CompileBasic()
        {
            var settings = new CodeSnippetPatcherSettings()
            {
                On = true,
                Code = @"// Let's do work! 
                    int wer = 23; 
                    wer++;",
                Nickname = "UnitTests",
            };
            var snippet = new CodeSnippetPatcherRun(settings);
            var result = snippet.Compile(GameRelease.SkyrimSE, CancellationToken.None, out var _);
            Assert.True(result.Success);
        }

        [Fact]
        public async Task CompileWithMutagenCore()
        {
            var settings = new CodeSnippetPatcherSettings()
            {
                On = true,
                Code = $"var modPath = {nameof(ModPath)}.{nameof(ModPath.Empty)}; modPath.Equals({nameof(ModPath)}.{nameof(ModPath.Empty)});",
                Nickname = "UnitTests",
            };
            var snippet = new CodeSnippetPatcherRun(settings);
            var result = snippet.Compile(GameRelease.SkyrimSE, CancellationToken.None, out var _);
            Assert.True(result.Success);
        }

        [Fact]
        public async Task CompileWithSpecificGames()
        {
            foreach (var game in EnumExt.GetValues<GameRelease>())
            {
                var settings = new CodeSnippetPatcherSettings()
                {
                    On = true,
                    Code = $"var id = {game.ToCategory()}Mod.DefaultInitialNextFormID; id++;",
                    Nickname = "UnitTests",
                };
                var snippet = new CodeSnippetPatcherRun(settings);
                var result = snippet.Compile(game, CancellationToken.None, out var _);
                Assert.True(result.Success);
            }
        }

        [Fact]
        public async Task BasicRun()
        {
            using var tmpFolder = Utility.GetTempFolder();
            using var dataFolder = Utility.SetupDataFolder(tmpFolder, GameRelease.Oblivion);
            var settings = new CodeSnippetPatcherSettings()
            {
                On = true,
                Code = @"// Let's do work! 
                    int wer = 23; 
                    wer++;",
                Nickname = "UnitTests",
            };
            var outputFile = Utility.TypicalOutputFile(tmpFolder);
            var snippet = new CodeSnippetPatcherRun(settings);
            await snippet.Prep(GameRelease.Oblivion);
            await snippet.Run(new RunSynthesisPatcher()
            {
                OutputPath = ModPath.FromPath(outputFile),
                DataFolderPath = dataFolder.Dir.Path,
                GameRelease = GameRelease.Oblivion,
                LoadOrderFilePath = Utility.PathToLoadOrderFile,
                SourcePaths = Enumerable.Empty<string>()
            });
        }

        [Fact]
        public async Task CreatesOutput()
        {
            using var tmpFolder = Utility.GetTempFolder();
            using var dataFolder = Utility.SetupDataFolder(tmpFolder, GameRelease.Oblivion);
            var settings = new CodeSnippetPatcherSettings()
            {
                On = true,
                Code = @"// Let's do work! 
                    int wer = 23; 
                    wer++;",
                Nickname = "UnitTests",
            };
            var outputFile = Utility.TypicalOutputFile(tmpFolder);
            var snippet = new CodeSnippetPatcherRun(settings);
            await snippet.Prep(GameRelease.Oblivion);
            await snippet.Run(new RunSynthesisPatcher()
            {
                OutputPath = ModPath.FromPath(outputFile),
                DataFolderPath = dataFolder.Dir.Path,
                GameRelease = GameRelease.Oblivion,
                LoadOrderFilePath = Utility.PathToLoadOrderFile,
                SourcePaths = Enumerable.Empty<string>()
            });
            Assert.True(File.Exists(outputFile));
        }

        [Fact]
        public async Task RunTwice()
        {
            using var tmpFolder = Utility.GetTempFolder();
            using var dataFolder = Utility.SetupDataFolder(tmpFolder, GameRelease.Oblivion);
            var outputFile = Utility.TypicalOutputFile(tmpFolder);
            var settings = new CodeSnippetPatcherSettings()
            {
                On = true,
                Code = @"state.PatchMod.Npcs.AddNew();",
                Nickname = "UnitTests",
            };
            for (int i = 0; i < 2; i++)
            {
                var snippet = new CodeSnippetPatcherRun(settings);
                await snippet.Prep(GameRelease.Oblivion);
                await snippet.Run(new RunSynthesisPatcher()
                {
                    OutputPath = ModPath.FromPath(outputFile),
                    DataFolderPath = dataFolder.Dir.Path,
                    GameRelease = GameRelease.Oblivion,
                    LoadOrderFilePath = Utility.PathToLoadOrderFile,
                    SourcePaths = i == 1 ? new string[] { outputFile } : Enumerable.Empty<string>()
                });
            }
            using var mod = OblivionMod.CreateFromBinaryOverlay(outputFile);
            Assert.Equal(2, mod.Npcs.Count);
        }

        [Fact]
        public void ConstructStateFactory()
        {
            using var tmpFolder = Utility.GetTempFolder();
            using var dataFolder = Utility.SetupDataFolder(tmpFolder, GameRelease.Oblivion);
            var output = Utility.TypicalOutputFile(tmpFolder);
            var settings = new RunSynthesisMutagenPatcher()
            {
                DataFolderPath = dataFolder.Dir.Path,
                GameRelease = GameRelease.Oblivion,
                LoadOrderFilePath = Utility.PathToLoadOrderFile,
                OutputPath = output,
                SourcePaths = Enumerable.Empty<string>()
            };
            var factory = CodeSnippetPatcherRun.ConstructStateFactory(GameRelease.Oblivion);
            var stateObj = factory(settings, new UserPreferences());
            Assert.NotNull(stateObj);
            using var state = stateObj as SynthesisState<IOblivionMod, IOblivionModGetter>;
            Assert.NotNull(state);
        }
    }
}
