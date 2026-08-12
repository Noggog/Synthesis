using System.Text.RegularExpressions;
using Synthesis.Bethesda.Execution.Exceptions;

namespace Synthesis.Bethesda.Execution.Reporters.Classifications;

/// <summary>
/// Detects nuget package signature validation failures (NU3037 / NU3028).
/// Must be checked before the generic CompilationExceptionDetector.
/// </summary>
public class NugetSignatureExceptionDetector : IExceptionClassificationDetector, IErrorClassificationDetector
{
    public ErrorClassification? IsApplicable(Exception exception)
    {
        var current = exception;
        while (current != null)
        {
            if (current is SynthesisBuildFailure buildFailure
                && NugetSignatureErrorClassification.IsSignatureFailure(buildFailure.Message))
            {
                return new NugetSignatureErrorClassification(
                    NugetSignatureErrorClassification.ExtractPackages(buildFailure.Message));
            }
            current = current.InnerException;
        }
        return null;
    }

    public ErrorClassification? IsApplicable(
        IReadOnlyList<string>? capturedOutput,
        IReadOnlyList<string>? capturedErrors)
    {
        var allLines = new List<string>();
        if (capturedOutput != null) allLines.AddRange(capturedOutput);
        if (capturedErrors != null) allLines.AddRange(capturedErrors);

        if (!allLines.Any(NugetSignatureErrorClassification.IsSignatureFailure)) return null;

        return new NugetSignatureErrorClassification(
            NugetSignatureErrorClassification.ExtractPackages(
                string.Join(Environment.NewLine, allLines)));
    }
}

/// <summary>
/// Classification for nuget package signature validation failures.
/// </summary>
public class NugetSignatureErrorClassification : ErrorClassification
{
    public const string ErrorTypeString = "Nuget Signature Validation Failure";
    public const string ExpiredSignatureCode = "NU3037";
    public const string UntrustedTimestampCode = "NU3028";
    public const string DiscussionUrl = "https://github.com/Mutagen-Modding/Synthesis/discussions/605";

    public const string SuggestionMessage =
        "Nuget refused to install one or more packages because it could not validate their repository signatures. " +
        "This is an environment problem rather than a problem with the patcher itself: the machine's certificate " +
        "trust store is out of date or unavailable, so nuget considers the signatures expired or untrusted. " +
        "It is most commonly seen when running Synthesis through Proton/Wine.\n\n" +
        "The linked discussion covers how to work around this, typically by refreshing the trusted root " +
        "certificates or turning off nuget signature validation.";

    private static readonly Regex PackagePattern = new Regex(
        @"Package '([^']+)'",
        RegexOptions.Compiled);

    private static readonly string[] Markers =
    {
        ExpiredSignatureCode,
        UntrustedTimestampCode,
        "signature validity period has expired",
        "timestamping certificate is not trusted",
    };

    public IReadOnlyList<string> Packages { get; }

    public NugetSignatureErrorClassification(IReadOnlyList<string>? packages = null)
    {
        Packages = packages ?? Array.Empty<string>();
    }

    public override string ErrorType => ErrorTypeString;
    public override string Message => SuggestionMessage;
    public override string? DiscussionLink => DiscussionUrl;

    public static bool IsSignatureFailure(string message)
    {
        foreach (var marker in Markers)
        {
            if (message.Contains(marker, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public static IReadOnlyList<string> ExtractPackages(string message)
    {
        var ret = new List<string>();
        foreach (var line in message.Split('\n'))
        {
            if (!IsSignatureFailure(line)) continue;
            var match = PackagePattern.Match(line);
            if (!match.Success) continue;
            var package = match.Groups[1].Value.Trim();
            if (package.Length == 0 || ret.Contains(package)) continue;
            ret.Add(package);
        }
        return ret;
    }
}
