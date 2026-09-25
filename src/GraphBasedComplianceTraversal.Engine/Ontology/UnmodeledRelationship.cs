namespace GraphBasedComplianceTraversal.Engine.Ontology;

/// <summary>
/// A relationship field in the repository fixtures that is intentionally not
/// modeled in this slice: the field's values reference entities outside the
/// declared node types (teams, roles, Terraform modules, fixture documents,
/// shared risk-library entries, control-definition clauses, incidents) or carry
/// free text rather than node references.
/// </summary>
public sealed record UnmodeledRelationship(NodeType SourceType, string FieldPath, string Reason);
