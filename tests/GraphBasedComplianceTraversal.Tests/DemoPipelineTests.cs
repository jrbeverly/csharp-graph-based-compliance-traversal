using GraphBasedComplianceTraversal.Cli;
using Xunit;

namespace GraphBasedComplianceTraversal.Tests;

/// <summary>
/// Pins the end-to-end demonstration: one command runs the whole pipeline
/// offline over the repository's own world — facts loaded, mocked
/// observations normalized, the typed graph, the three canonical paths,
/// assertion evaluation, the observation store's current state, the linter
/// findings, and the compiled supply-chain narrative — as a single
/// deterministic document golden-pinned byte for byte, with each of the six
/// seeded imperfections visible rather than a perfect story. Re-running
/// produces identical output, and a repository that cannot be loaded fails
/// cleanly with a non-zero exit code.
/// </summary>
public sealed class DemoPipelineTests
{
    [Fact]
    public void TheDemoCommandRunsTheFullPipelineOfflineAndMatchesTheGoldenFile()
    {
        var exitCode = RunDemo(out var output, out var error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Golden.AssertMatches("demo.txt", output);
    }

    [Fact]
    public void ReRunningTheDemoProducesIdenticalOutput()
    {
        RunDemo(out var first, out _);
        RunDemo(out var second, out _);

        Assert.Equal(first, second);
    }

    [Fact]
    public void TheDemoDemonstratesEachOfTheTenPlanningProofPoints()
    {
        RunDemo(out var output, out _);

        // Facts loaded and mocked observations normalized.
        Assert.Contains("21 declared records under data/", output, StringComparison.Ordinal);
        Assert.Contains("8 mocked external responses under fixtures/", output, StringComparison.Ordinal);
        Assert.Contains("normalized nodes and", output, StringComparison.Ordinal);

        // Typed connected graph.
        Assert.Contains("3. Typed connected graph (TypedPropertyGraph)", output, StringComparison.Ordinal);
        Assert.Contains("nodes and", output, StringComparison.Ordinal);

        // The three canonical paths, by name.
        Assert.Contains("4. Canonical path 1 of 3 — business ↔ infrastructure", output, StringComparison.Ordinal);
        Assert.Contains("5. Canonical path 2 of 3 — infrastructure → source and provenance", output, StringComparison.Ordinal);
        Assert.Contains("6. Canonical path 3 of 3 — risk → control and observations", output, StringComparison.Ordinal);

        // Assertion evaluation, with the honest UNKNOWN states kept.
        Assert.Contains("7. Assertion evaluation", output, StringComparison.Ordinal);
        Assert.Contains("→ PASS —", output, StringComparison.Ordinal);
        Assert.Contains("→ UNKNOWN —", output, StringComparison.Ordinal);

        // The observation store's current state.
        Assert.Contains("8. Observation store — current state per assertion", output, StringComparison.Ordinal);

        // Detection of incomplete/invalid state: the linter findings.
        Assert.Contains("9. Organizational linter findings", output, StringComparison.Ordinal);

        // The requirement traced through real controls and observations, as
        // the compiled explainable narrative.
        Assert.Contains("10. Compiled supply-chain narrative", output, StringComparison.Ordinal);
        Assert.Contains("is satisfied by", output, StringComparison.Ordinal);
        Assert.Contains("is established: 5 validated paths", output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDemoSurfacesEachOfTheSixSeededImperfections()
    {
        RunDemo(out var output, out _);

        // #1 — the failed provenance observation.
        Assert.Contains(
            "observation:obs-2026-08-06-001 → fail (recorded fail",
            output,
            StringComparison.Ordinal);
        Assert.Contains("related incident INC-2026-041", output, StringComparison.Ordinal);

        // #2 — the stale observations from outdated validators.
        Assert.Contains("→ stale (recorded pass", output, StringComparison.Ordinal);

        // #3 — the expiring emergency-deployment exception.
        Assert.Contains("expires on 2026-09-01, within 30 days of the reference date", output, StringComparison.Ordinal);

        // #4 — the stale supplier risk assessment.
        Assert.Contains(
            "last_risk_assessment 2026-06-01 is 72 days before the reference date",
            output,
            StringComparison.Ordinal);

        // #5 — the missing supplier-to-requirement mapping.
        Assert.Contains(
            "its satisfaction chain reaches supplier:aws, but the requirement declares no mapping to that supplier",
            output,
            StringComparison.Ordinal);

        // #6 — the unresolved generic risk reference.
        Assert.Contains("ARR-RISK-044", output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCompiledNarrativeKeepsUnresolvedAndNonPassingStateVisible()
    {
        RunDemo(out var output, out _);

        // The recorded failure and the outdated-validator staleness stay
        // visible in the compiled prose, and the CONFLICTING reconciliations
        // stay visible in the linter section.
        Assert.Contains("recorded fail at", output, StringComparison.Ordinal);
        Assert.Contains("The store currently surfaces stale (recorded pass).", output, StringComparison.Ordinal);
        Assert.Contains("CONFLICTING", output, StringComparison.Ordinal);
        Assert.Contains("UNKNOWN", output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDemoFailsCleanlyWhenTheRepositoryCannotBeLoaded()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = CliApplication.Run(
            ["demo"],
            output,
            error,
            Path.Combine(Path.GetTempPath(), "no-such-repository-here"));

        Assert.NotEqual(0, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("demo failed:", error.ToString(), StringComparison.Ordinal);
    }

    private static int RunDemo(out string output, out string error)
    {
        var outputWriter = new StringWriter();
        var errorWriter = new StringWriter();
        try
        {
            var exitCode = CliApplication.Run(["demo"], outputWriter, errorWriter, TestWorld.RepositoryRoot);
            output = outputWriter.ToString();
            error = errorWriter.ToString();
            return exitCode;
        }
        finally
        {
            outputWriter.Dispose();
            errorWriter.Dispose();
        }
    }
}
