using GraphBasedComplianceTraversal.Engine.State;
using GraphBasedComplianceTraversal.Engine.Traversal;

namespace GraphBasedComplianceTraversal.Engine.Views;

/// <summary>
/// The deterministic textual rendering of a <see cref="GraphView"/>: the
/// view's name and subject, the explained paths it is projected from
/// (rendered by <see cref="ExplainedPath"/>, so every hop names the typed
/// edge that justifies it), and — for a view that carries observation state
/// — each observation with the state the store surfaces, the state it was
/// recorded with, and the validator version that produced it. The rendering
/// is a structured inspection format for pinning the views in golden files;
/// prose narrative rendering is a later milestone's concern.
/// </summary>
public static class ViewRendering
{
    /// <summary>
    /// Renders the view: the name and subject line, the explained paths,
    /// and the current observation state when the view carries any.
    /// Deterministic — the same view always renders the same text.
    /// </summary>
    public static string Render(GraphView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var sections = new List<string> { $"{Capitalize(view.Name)} view of {view.Subject.Id}" };
        if (view.Paths.Count > 0)
        {
            sections.Add(ExplainedPath.Render(view.Paths));
        }

        if (view.Observations.Count > 0)
        {
            sections.Add(
                "Current observation state:" +
                string.Join(string.Empty, view.Observations.Select(RenderObservation)));
        }

        return string.Join("\n\n", sections);
    }

    private static string RenderObservation(ViewObservation observation)
    {
        var line = $"\n  {observation.ObservationId} {StateName(observation.State)}";
        if (observation.IsStale)
        {
            line += $" (recorded {StateName(observation.RecordedState)}, validator {observation.ValidatorVersion})";
        }

        return line;
    }

    private static string StateName(ValidationState state) =>
        state.ToString().ToLowerInvariant();

    private static string Capitalize(string name) =>
        char.ToUpperInvariant(name[0]) + name[1..];
}
