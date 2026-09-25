using System.Text.Json.Nodes;

namespace GraphBasedComplianceTraversal.Engine.State;

/// <summary>
/// A graph subject (a resource, artifact, service, ...) carrying two
/// independent fact sets over the same property vocabulary: the facts the
/// organization <em>declares</em> should exist, and the facts an external
/// system <em>observes</em> actually exists. Both sets address the same
/// property names, so one property can hold a declared value and a differing
/// observed value simultaneously — recording one never overwrites the other.
/// Values are JSON nodes, the same value representation used by the IO layer.
/// A JSON null value carries no information and is treated as absent.
/// </summary>
public sealed class FactSubject
{
    private readonly Dictionary<string, JsonNode?> _declared = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonNode?> _observed = new(StringComparer.Ordinal);

    /// <summary>
    /// Creates an empty subject, optionally seeding the declared and observed
    /// fact sets. A repeated property name in a seed keeps the last value,
    /// like a subsequent <see cref="Declare"/> or <see cref="Observe"/> call.
    /// </summary>
    public FactSubject(
        string id,
        IEnumerable<KeyValuePair<string, JsonNode?>>? declared = null,
        IEnumerable<KeyValuePair<string, JsonNode?>>? observed = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;

        if (declared is not null)
        {
            foreach (var (property, value) in declared)
            {
                Declare(property, value);
            }
        }

        if (observed is not null)
        {
            foreach (var (property, value) in observed)
            {
                Observe(property, value);
            }
        }
    }

    /// <summary>
    /// The stable identifier of the subject, for example
    /// <c>aws:s3:prod-release-artifacts</c>.
    /// </summary>
    public string Id { get; }

    /// <summary>The declared facts, keyed by property name.</summary>
    public IReadOnlyDictionary<string, JsonNode?> Declared => _declared;

    /// <summary>The observed facts, keyed by property name.</summary>
    public IReadOnlyDictionary<string, JsonNode?> Observed => _observed;

    /// <summary>
    /// Records a declared fact for a property. This writes only the declared
    /// set; any observed value for the same property is left untouched.
    /// </summary>
    public void Declare(string property, JsonNode? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(property);
        _declared[property] = value;
    }

    /// <summary>
    /// Records an observed fact for a property. This writes only the observed
    /// set; any declared value for the same property is left untouched.
    /// </summary>
    public void Observe(string property, JsonNode? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(property);
        _observed[property] = value;
    }

    /// <summary>True when the property carries a declared value.</summary>
    public bool HasDeclaration(string property)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(property);
        return _declared.ContainsKey(property);
    }

    /// <summary>True when the property carries an observed value.</summary>
    public bool HasObservation(string property)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(property);
        return _observed.ContainsKey(property);
    }

    /// <summary>
    /// Resolves a property to a validation state:
    /// <list type="bullet">
    /// <item>no observed value → <see cref="ValidationState.Unknown"/> — absence
    /// of an observation is never an implied pass, no matter what is declared;</item>
    /// <item>observed without a declared value → <see cref="ValidationState.Unknown"/> —
    /// there is no declared expectation to validate the observation against;</item>
    /// <item>declared and observed values deep-equal → <see cref="ValidationState.Pass"/>;</item>
    /// <item>declared and observed values differ → <see cref="ValidationState.Conflicting"/>.</item>
    /// </list>
    /// States such as <see cref="ValidationState.Fail"/>,
    /// <see cref="ValidationState.NotApplicable"/>, and
    /// <see cref="ValidationState.Expired"/> are decided by specific
    /// invariants (the Assertions milestone), not by this comparison.
    /// </summary>
    public ResolvedFact Resolve(string property)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(property);

        _declared.TryGetValue(property, out var declared);
        _observed.TryGetValue(property, out var observed);

        if (observed is null)
        {
            // No observation: the property is unchecked. A declared value is
            // intention, not evidence — it can never resolve to a pass on its own.
            return new ResolvedFact(property, ValidationState.Unknown, declared, null);
        }

        if (declared is null)
        {
            // Observed without a declared expectation: nothing to validate against.
            return new ResolvedFact(property, ValidationState.Unknown, null, observed);
        }

        var state = JsonNode.DeepEquals(declared, observed)
            ? ValidationState.Pass
            : ValidationState.Conflicting;

        return new ResolvedFact(property, state, declared, observed);
    }

    /// <summary>
    /// Resolves every property in the union of the declared and observed
    /// sets, ordered by property name.
    /// </summary>
    public IReadOnlyList<ResolvedFact> ResolveAll() =>
        _declared.Keys.Concat(_observed.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(property => property, StringComparer.Ordinal)
            .Select(Resolve)
            .ToArray();
}
