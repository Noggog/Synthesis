using Mutagen.Bethesda;
using Shouldly;
using Synthesis.Bethesda.CLI.RunPipeline;
using Synthesis.Bethesda.Execution.Exceptions;
using Synthesis.Bethesda.Execution.Reporters.Classifications;
using Synthesis.Bethesda.GUI.ViewModels.Errors;
using Synthesis.Bethesda.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace Synthesis.Bethesda.IntegrationTests.Errors;

/// <summary>
/// Abstract base for nuget signature validation error detection tests (NU3037 / NU3028).
/// </summary>
public abstract class NugetSignatureErrorTest : IntegrationTest
{
    protected const string ExpectedPackage = "K4os.Compression.LZ4.Streams 1.2.2-beta";

    protected NugetSignatureErrorTest(ITestOutputHelper output) : base(output)
    {
    }

    protected abstract override PipelineMode Mode { get; }

    [Fact]
    public async Task NugetSignatureError_IsDetectedAndReported()
    {
        // Arrange
        var patcher = CreateSolutionPatcherWithSettings(
            "NugetSignaturePatcher",
            GenerateNugetSignaturePatchContent(),
            nickname: "Nuget Signature Patcher");

        ExportSettingsWithPatchers(
            groupName: "Test Group",
            patchers: new[] { patcher });

        // Act
        await Act();

        // Assert
        await AssertErrorOccurred();
    }

    private static Action<GameRelease, Noggog.StructuredStrings.StructuredStringBuilder> GenerateNugetSignaturePatchContent()
    {
        return (gameRelease, sb) =>
        {
            sb.AppendLine("throw new System.Exception(");
            sb.AppendLine("    @\"C:\\Temp\\Synthesis\\VersionQuery\\VersionQuery.csproj : error NU3037: \" +");
            sb.AppendLine("    @\"Package 'K4os.Compression.LZ4.Streams 1.2.2-beta' from source \" +");
            sb.AppendLine("    @\"'https://api.nuget.org/v3/index.json': The repository primary signature validity period has expired.\");");
        };
    }

    protected abstract Task Act();

    protected virtual Task AssertErrorOccurred()
    {
        return Task.CompletedTask;
    }
}

public class NugetSignatureErrorUIPipelineTest : NugetSignatureErrorTest
{
    public NugetSignatureErrorUIPipelineTest(ITestOutputHelper output) : base(output)
    {
    }

    protected override PipelineMode Mode => PipelineMode.UI;

    protected override async Task Act()
    {
        await RunPatcherPipeline();
    }

    protected override async Task AssertErrorOccurred()
    {
        var payload = GetStoredPayload();

        payload.ActiveRunVm.CurrentRun.ShouldNotBeNull("CurrentRun should be set");

        var patcherRun = payload.ActiveRunVm.CurrentRun.Groups
            .SelectMany(g => g.Patchers)
            .FirstOrDefault();

        patcherRun.ShouldNotBeNull("Should have at least one patcher run");

        patcherRun.State.Value.ShouldBe(Synthesis.Bethesda.GUI.ViewModels.Profiles.Running.RunState.Error,
            "Patcher should be in Error state");

        patcherRun.ErrorClassification.ShouldNotBeNull("ErrorClassification should be populated");
        var vm = patcherRun.ErrorClassification.ShouldBeOfType<NugetSignatureErrorVm>(
            "ErrorClassification should be NugetSignatureErrorVm");

        Output.WriteLine($"Error Classification Type: {vm.ErrorType}");
        Output.WriteLine($"Error Message: {vm.Message}");
        Output.WriteLine($"Packages: {string.Join(", ", vm.Packages)}");
        vm.ErrorType.ShouldBe(NugetSignatureErrorClassification.ErrorTypeString);
        vm.DiscussionLink.ShouldBe(NugetSignatureErrorClassification.DiscussionUrl);
        vm.Packages.ShouldBe(new[] { ExpectedPackage });

        await Task.CompletedTask;
    }
}

public class NugetSignatureErrorCliPipelineTest : NugetSignatureErrorTest
{
    private Exception? _caughtException;

    public NugetSignatureErrorCliPipelineTest(ITestOutputHelper output) : base(output)
    {
    }

    protected override PipelineMode Mode => PipelineMode.CLI;

    protected override async Task Act()
    {
        var runPipeline = GetComponentPayload<RunPatcherPipeline, object>();

        try
        {
            await runPipeline.Run(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _caughtException = ex;
            Output.WriteLine($"Run completed with expected error: {ex.Message}");
        }
    }

    protected override Task AssertErrorOccurred()
    {
        _caughtException.ShouldNotBeNull("Expected an exception to be thrown during patcher execution");
        _caughtException.ShouldBeOfType<ClassifiedErrorException>();

        Output.WriteLine(LogSink.GetFullLog());

        var errorMessages = LogSink.ErrorMessages;
        errorMessages.ShouldContain(msg => msg.Contains("Error detected:") && msg.Contains(NugetSignatureErrorClassification.ErrorTypeString),
            "Should have logged the nuget signature error classification");
        errorMessages.ShouldContain(msg => msg.Contains("could not validate their repository signatures"),
            "Should have logged the error suggestion");

        return Task.CompletedTask;
    }
}
