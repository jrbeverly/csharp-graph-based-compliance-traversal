namespace GraphBasedComplianceTraversal.Engine.Ontology;

/// <summary>
/// A typed edge declared by the ontology slice: a directed relationship with an
/// explicit forward name and inverse name connecting exactly two declared node
/// types. Names are directional — <see cref="ForwardName"/> reads from
/// <see cref="FromType"/> to <see cref="ToType"/>, and <see cref="InverseName"/>
/// reads back from <see cref="ToType"/> to <see cref="FromType"/>. Two edge
/// types may share a name when their endpoint types differ (for example
/// <c>depends-on</c> from a service to an S3 bucket versus a CloudFront
/// distribution); resolution is always disambiguated by the endpoints.
/// </summary>
public sealed record EdgeType(
    string ForwardName,
    string InverseName,
    NodeType FromType,
    NodeType ToType,
    string Description);
