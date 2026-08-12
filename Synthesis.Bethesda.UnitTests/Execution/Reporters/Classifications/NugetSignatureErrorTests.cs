using Shouldly;
using Synthesis.Bethesda.Execution.Exceptions;
using Synthesis.Bethesda.Execution.Reporters.Classifications;

namespace Synthesis.Bethesda.UnitTests.Execution.Reporters.Classifications;

public class NugetSignatureErrorTests
{
    private const string ExpiredLine =
        @"C:\users\steamuser\AppData\Local\Temp\Synthesis\VersionQuery\VersionQuery.csproj : error NU3037: Package 'K4os.Compression.LZ4.Streams 1.2.2-beta' from source 'https://api.nuget.org/v3/index.json': The repository primary signature validity period has expired.";

    private const string UntrustedLine =
        @"C:\users\steamuser\AppData\Local\Temp\Synthesis\VersionQuery\VersionQuery.csproj : error NU3028: Package 'K4os.Compression.LZ4.Streams 1.2.2-beta' from source 'https://api.nuget.org/v3/index.json': The repository primary signature's timestamping certificate is not trusted by the trust provider.";

    [Fact]
    public void DetectsExpiredSignature()
    {
        NugetSignatureErrorClassification.IsSignatureFailure(ExpiredLine).ShouldBeTrue();
    }

    [Fact]
    public void DetectsUntrustedTimestamp()
    {
        NugetSignatureErrorClassification.IsSignatureFailure(UntrustedLine).ShouldBeTrue();
    }

    [Fact]
    public void IgnoresUnrelatedError()
    {
        NugetSignatureErrorClassification.IsSignatureFailure(
                "error CS0029: Cannot implicitly convert type 'int' to 'string'")
            .ShouldBeFalse();
    }

    [Fact]
    public void ExtractsDistinctPackages()
    {
        NugetSignatureErrorClassification.ExtractPackages(
                $"{ExpiredLine}\n{UntrustedLine}")
            .ShouldBe(new[] { "K4os.Compression.LZ4.Streams 1.2.2-beta" });
    }

    [Fact]
    public void ExtractsNoPackagesFromUnrelatedLines()
    {
        NugetSignatureErrorClassification.ExtractPackages(
                "Package 'Some.Package 1.0.0' installed fine")
            .ShouldBeEmpty();
    }

    [Fact]
    public void DetectorClassifiesCapturedOutput()
    {
        var sut = new NugetSignatureExceptionDetector();

        var classification = sut.IsApplicable(new[] { "Build FAILED", ExpiredLine }, null);

        var typed = classification.ShouldBeOfType<NugetSignatureErrorClassification>();
        typed.ErrorType.ShouldBe(NugetSignatureErrorClassification.ErrorTypeString);
        typed.DiscussionLink.ShouldBe(NugetSignatureErrorClassification.DiscussionUrl);
        typed.Packages.ShouldBe(new[] { "K4os.Compression.LZ4.Streams 1.2.2-beta" });
    }

    [Fact]
    public void DetectorClassifiesBuildFailure()
    {
        var sut = new NugetSignatureExceptionDetector();

        sut.IsApplicable(new SynthesisBuildFailure(ExpiredLine))
            .ShouldBeOfType<NugetSignatureErrorClassification>();
    }

    [Fact]
    public void CompilationDetectorDefersToSignatureFailure()
    {
        new CompilationExceptionDetector()
            .IsApplicable(new SynthesisBuildFailure(ExpiredLine))
            .ShouldBeNull();
    }
}
