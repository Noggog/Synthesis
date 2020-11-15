using CommandLine;
using Mutagen.Bethesda;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Synthesis.Bethesda
{
    [Verb("run-patcher", HelpText = "Run the patcher")]
    public class RunSynthesisPatcher
    {
        [Option('s', "SourcePaths", Required = false, HelpText = "Optional path(s) pointing to the previous patcher result(s) to build onto.")]
        public IEnumerable<string> SourcePaths { get; set; } = Enumerable.Empty<string>();

        [Option('o', "OutputPath", Required = true, HelpText = "Path where the patcher should place its resulting file.")]
        public string OutputPath { get; set; } = string.Empty;

        [Option('g', "GameRelease", Required = true, HelpText = "GameRelease data folder is related to.")]
        public GameRelease GameRelease { get; set; }

        [Option('d', "DataFolderPath", Required = true, HelpText = "Path to the data folder.")]
        public string DataFolderPath { get; set; } = string.Empty;

        [Option('l', "LoadOrderFilePath", Required = false, HelpText = "Path to the load order file to use.")]
        public string LoadOrderFilePath { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"{nameof(RunSynthesisPatcher)} => \n"
                + $"  {nameof(SourcePaths)} => {this.SourcePaths} \n"
                + $"  {nameof(OutputPath)} => {this.OutputPath} \n"
                + $"  {nameof(GameRelease)} => {this.GameRelease} \n"
                + $"  {nameof(DataFolderPath)} => {this.DataFolderPath} \n"
                + $"  {nameof(LoadOrderFilePath)} => {this.LoadOrderFilePath}";
        }
    }
}
