using System.Globalization;

namespace GraphBasedComplianceTraversal.Engine.Observations;

/// <summary>
/// Parsing and comparison of validator version strings — the
/// <c>name@major.minor.patch</c> form the recorded observations carry (for
/// example <c>provenance-prober@3.1.7</c>). The validator name is the part
/// before the <c>@</c>; the version is the dotted sequence of non-negative
/// integers after it, compared segment-wise numerically so <c>3.1.10</c>
/// outranks <c>3.1.7</c>.
/// </summary>
internal static class ValidatorVersion
{
    /// <summary>
    /// True when the string names a validator and a version: a non-empty name
    /// before a single <c>@</c>, and one or more dot-separated non-negative
    /// integer segments after it.
    /// </summary>
    public static bool IsValid(string version)
    {
        var at = version.IndexOf('@');
        if (at <= 0 || at == version.Length - 1)
        {
            return false;
        }

        var segments = version[(at + 1)..].Split('.');
        return segments.All(segment =>
            segment.Length > 0
            && long.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out _));
    }

    /// <summary>The validator name before the <c>@</c>, or <c>null</c> when none separates one.</summary>
    public static string? NameOf(string version)
    {
        var at = version.IndexOf('@');
        return at > 0 ? version[..at] : null;
    }

    /// <summary>
    /// Compares the version parts segment-wise numerically: a missing segment
    /// counts as zero (<c>3.1</c> equals <c>3.1.0</c>), and a segment that is
    /// not a number compares ordinally against the segment it faces.
    /// </summary>
    public static int Compare(string left, string right)
    {
        var leftSegments = Segments(left);
        var rightSegments = Segments(right);
        for (var index = 0; index < Math.Max(leftSegments.Length, rightSegments.Length); index++)
        {
            var leftSegment = index < leftSegments.Length ? leftSegments[index] : "0";
            var rightSegment = index < rightSegments.Length ? rightSegments[index] : "0";
            if (long.TryParse(leftSegment, NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber)
                && long.TryParse(rightSegment, NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber))
            {
                if (leftNumber != rightNumber)
                {
                    return leftNumber.CompareTo(rightNumber);
                }

                continue;
            }

            var compared = string.Compare(leftSegment, rightSegment, StringComparison.Ordinal);
            if (compared != 0)
            {
                return compared;
            }
        }

        return 0;
    }

    private static string[] Segments(string version)
    {
        var at = version.IndexOf('@');
        return (at >= 0 ? version[(at + 1)..] : version).Split('.');
    }
}
