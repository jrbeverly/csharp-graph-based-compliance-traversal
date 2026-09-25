using GraphBasedComplianceTraversal.Engine.Ontology;

namespace GraphBasedComplianceTraversal.Engine.Traversal;

/// <summary>
/// The canonical path grammars the planning input names, each a ready-built
/// <see cref="PathGrammar"/> over the ontology slice — declared once, in one
/// place, so every component that needs the same semantic path compiles
/// against the same declaration instead of restating it.
/// <list type="bullet">
/// <item><see cref="SupplyChainAssurance"/> — the supply-chain assurance
/// path: from a regulatory requirement, <c>satisfied-by</c> to the control
/// adoption, <c>implemented-by</c> to the implementation,
/// <c>validated-by</c> to the assertion, and <c>produces</c> to its
/// observations.</item>
/// </list>
/// </summary>
public static class CanonicalPathGrammars
{
    /// <summary>
    /// The supply-chain assurance grammar evaluated by every consumer that
    /// concludes a requirement is addressed through the organization's
    /// actual control machinery: the requirement's satisfying control
    /// adoption, the implementation realizing it, the assertion validating
    /// it, and the observations the assertion produced — a conclusion is
    /// only reached through this typed edge sequence, never an arbitrary
    /// hop between the same endpoints.
    /// </summary>
    public static PathGrammar SupplyChainAssurance(EdgeTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return new PathGrammar(registry, "supply-chain-assurance")
            .Step("satisfied-by", Node(registry, "RegulatoryRequirement"), Node(registry, "ControlAdoption"))
            .Step("implemented-by", Node(registry, "ControlAdoption"), Node(registry, "ControlImplementation"))
            .Step("validated-by", Node(registry, "ControlImplementation"), Node(registry, "Assertion"))
            .Step("produces", Node(registry, "Assertion"), Node(registry, "Observation"));
    }

    private static NodeType Node(EdgeTypeRegistry registry, string name) =>
        registry.NodeTypes.Single(type => type.Name == name);
}
