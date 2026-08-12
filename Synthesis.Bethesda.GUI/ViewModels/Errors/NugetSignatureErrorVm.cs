using Synthesis.Bethesda.Execution.Reporters.Classifications;
using Synthesis.Bethesda.GUI.Services.Main;

namespace Synthesis.Bethesda.GUI.ViewModels.Errors;

public class NugetSignatureErrorVm : ErrorClassificationVm
{
    public IReadOnlyList<string> Packages { get; }
    public bool HasPackages => Packages.Count > 0;

    public delegate NugetSignatureErrorVm Factory(NugetSignatureErrorClassification error);

    public NugetSignatureErrorVm(
        NugetSignatureErrorClassification error,
        INavigateTo navigateTo)
        : base(error, navigateTo)
    {
        Packages = error.Packages;
    }
}
