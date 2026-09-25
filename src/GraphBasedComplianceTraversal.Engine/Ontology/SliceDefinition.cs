namespace GraphBasedComplianceTraversal.Engine.Ontology;

/// <summary>
/// The authoritative definition of the minimal ontology slice this repository
/// compiles against: the node types, the typed edge types (each with a forward
/// name, an inverse name, and declared endpoint node types), the mapping from
/// the fixture relationship field vocabulary onto canonical edge types, and the
/// relationship fields intentionally unmodeled in this slice. docs/ontology.md
/// renders this list for humans; this class is the contract code compiles
/// against, and the test suite pins it to the fixtures in data/**.
/// </summary>
internal static class SliceDefinition
{
    public static EdgeTypeRegistry Create()
    {
        // Node types — one entry per concept the slice supports. IdPrefix is the
        // canonical identifier prefix used by the fixture records.
        var businessPurpose = Node("BusinessPurpose", "business-purpose", "Business", "Why a set of systems exists: an organizational goal that products serve.");
        var product = Node("Product", "product", "Business", "A customer-facing product the organization operates.");
        var productCapability = Node("ProductCapability", "capability", "Business", "A discrete capability a product provides.");
        var service = Node("Service", "service", "Technical", "A system or service that realizes capabilities.");
        var sourceRepository = Node("SourceRepository", "repository", "Technical", "A source code repository.");
        var buildPipeline = Node("BuildPipeline", "build-pipeline", "Technical", "A CI/CD pipeline that builds and publishes artifacts.");
        var buildRun = Node("BuildRun", "build-run", "Technical", "A single CI workflow run of a build pipeline.");
        var commit = Node("Commit", "commit", "Technical", "A source commit in a repository.");
        var artifact = Node("Artifact", "artifact", "Technical", "A built, signed, publishable software artifact.");
        var cloudFrontDistribution = Node("CloudFrontDistribution", "aws:cloudfront", "Cloud", "A CloudFront CDN distribution serving release artifacts.");
        var s3Bucket = Node("S3Bucket", "aws:s3", "Cloud", "An S3 bucket storing release artifacts.");
        var supplier = Node("Supplier", "supplier", "Supplier", "An external supplier of infrastructure or services.");
        var risk = Node("Risk", "risk", "Security", "A risk of an undesirable outcome.");
        var businessImpact = Node("BusinessImpact", "impact", "Security", "Why a risk matters to the organization.");
        var assumption = Node("Assumption", "assumption", "Security", "An explicit assumption underlying assessments.");
        var controlDefinition = Node("ControlDefinition", "control-def", "Security", "An external, reusable control specification.");
        var controlAdoption = Node("ControlAdoption", "control-adoption", "Security", "The organization's adoption of a control definition.");
        var controlImplementation = Node("ControlImplementation", "implementation", "Security", "How an adopted control is realized.");
        var controlException = Node("ControlException", "exception", "Security", "A scoped, time-bounded deviation from a control adoption.");
        var assertion = Node("Assertion", "assertion", "Security", "A property expected to remain true, made testable.");
        var observation = Node("Observation", "observation", "Security", "A measured evaluation result of an assertion at a point in time.");
        var regulatoryFramework = Node("RegulatoryFramework", "framework", "Compliance", "A regulatory framework or directive.");
        var regulatoryRequirement = Node("RegulatoryRequirement", "requirement", "Compliance", "A requirement within a regulatory framework.");

        // Edge types — forward name reads from -> to; inverse name reads back.
        var fulfills = Edge("fulfills", "fulfilled-by", product, businessPurpose, "A product exists to fulfill a business purpose.");
        var hasCapability = Edge("has-capability", "belongs-to", product, productCapability, "A product exposes a capability.");
        var capabilityImplementedBy = Edge("implemented-by", "implements", productCapability, service, "A capability is realized by a service.");
        var adoptionImplementedBy = Edge("implemented-by", "implements", controlAdoption, controlImplementation, "An adopted control is realized by an implementation.");
        var producedBy = Edge("produced-by", "produces", artifact, buildPipeline, "A release artifact is produced by the build pipeline that created it.");
        var producedByRun = Edge("produced-by", "produces", artifact, buildRun, "A release artifact is produced by the build run that built it.");
        var builtFrom = Edge("built-from", "built-into", buildRun, commit, "A build run is built from the commit at its head.");
        var committedTo = Edge("committed-to", "has-commits", commit, sourceRepository, "A commit was committed to a source repository.");
        var storedIn = Edge("stored-in", "contains", artifact, s3Bucket, "A release artifact is stored in a bucket.");
        var distributedVia = Edge("distributed-via", "distributes", artifact, cloudFrontDistribution, "A release artifact reaches customers via a distribution.");
        var source = Edge("source", "builds", buildPipeline, sourceRepository, "A build pipeline builds from a source repository.");
        var dependsOnBucket = Edge("depends-on", "used-by", service, s3Bucket, "A service depends on a bucket.");
        var dependsOnDistribution = Edge("depends-on", "serves", service, cloudFrontDistribution, "A service depends on a distribution.");
        var origin = Edge("origin", "served-by", cloudFrontDistribution, s3Bucket, "A distribution's origin is a bucket.");
        var supplies = Edge("supplies", "supplied-by", supplier, service, "A supplier supplies (hosts or provides) a service.");
        var affects = Edge("affects", "affected-by", risk, product, "A risk affects a product.");
        var resultsIn = Edge("results-in", "results-from", risk, businessImpact, "A risk results in a business impact.");
        var mitigates = Edge("mitigates", "mitigated-by", controlAdoption, risk, "An adopted control mitigates a risk.");
        var informs = Edge("informs", "informed-by", businessImpact, controlAdoption, "A business impact informs a control adoption decision.");
        var assumptionSupportsRisk = Edge("supports", "supported-by", assumption, risk, "An assumption supports a risk assessment.");
        var assumptionSupportsImpact = Edge("supports", "supported-by", assumption, businessImpact, "An assumption supports a business impact assessment.");
        var derivedFrom = Edge("derived-from", "adopted-by", controlAdoption, controlDefinition, "An adoption derives from an external control definition.");
        var adoptionAppliesToPipeline = Edge("applies-to", "in-scope-of", controlAdoption, buildPipeline, "An adopted control applies to a build pipeline.");
        var adoptionAppliesToArtifact = Edge("applies-to", "in-scope-of", controlAdoption, artifact, "An adopted control applies to an artifact.");
        var adoptionAppliesToBucket = Edge("applies-to", "in-scope-of", controlAdoption, s3Bucket, "An adopted control applies to a bucket.");
        var adoptionAppliesToDistribution = Edge("applies-to", "in-scope-of", controlAdoption, cloudFrontDistribution, "An adopted control applies to a distribution.");
        var exceptionAppliesToService = Edge("applies-to", "in-scope-of", controlException, service, "An exception applies to a service.");
        var exceptionAppliesToBucket = Edge("applies-to", "in-scope-of", controlException, s3Bucket, "An exception applies to a bucket.");
        var validatesAdoption = Edge("validates", "validated-by", assertion, controlAdoption, "An assertion makes an adopted control testable.");
        var validatesImplementation = Edge("validates", "validated-by", assertion, controlImplementation, "An assertion validates an implementation.");
        var producesObservations = Edge("produces", "produced-by", assertion, observation, "Evaluating an assertion produces observations.");
        var observes = Edge("observes", "observed-by", observation, artifact, "An observation measures an artifact.");
        var requires = Edge("requires", "belongs-to", regulatoryFramework, regulatoryRequirement, "A framework requires a requirement.");
        var satisfies = Edge("satisfies", "satisfied-by", controlAdoption, regulatoryRequirement, "An adopted control satisfies a regulatory requirement.");
        var exceptionTo = Edge("exception-to", "has-exception", controlException, controlAdoption, "An exception patches a control adoption.");

        NodeType[] nodeTypes =
        [
            businessPurpose, product, productCapability,
            service, sourceRepository, buildPipeline, buildRun, commit, artifact,
            cloudFrontDistribution, s3Bucket,
            supplier,
            risk, businessImpact, assumption,
            controlDefinition, controlAdoption, controlImplementation, controlException,
            assertion, observation,
            regulatoryFramework, regulatoryRequirement,
        ];

        EdgeType[] edgeTypes =
        [
            fulfills, hasCapability,
            capabilityImplementedBy, adoptionImplementedBy,
            producedBy, producedByRun, builtFrom, committedTo, storedIn, distributedVia,
            source,
            dependsOnBucket, dependsOnDistribution,
            origin, supplies,
            affects, resultsIn, mitigates, informs,
            assumptionSupportsRisk, assumptionSupportsImpact,
            derivedFrom,
            adoptionAppliesToPipeline, adoptionAppliesToArtifact, adoptionAppliesToBucket, adoptionAppliesToDistribution,
            exceptionAppliesToService, exceptionAppliesToBucket,
            validatesAdoption, validatesImplementation,
            producesObservations, observes,
            requires, satisfies, exceptionTo,
        ];

        RelationshipFieldMapping[] fieldMappings =
        [
            // Business.
            Inverse(businessPurpose, "fulfilled_by", product, fulfills),
            Forward(product, "fulfills", businessPurpose, fulfills),
            Forward(product, "capabilities", productCapability, hasCapability),
            Inverse(productCapability, "belongs_to", product, hasCapability),
            Forward(productCapability, "implemented_by", service, capabilityImplementedBy),
            Inverse(service, "implements", productCapability, capabilityImplementedBy),

            // Technical.
            Forward(artifact, "built_by", buildPipeline, producedBy, "Fixture synonym for 'produced-by'."),
            Forward(artifact, "stored_in", s3Bucket, storedIn),
            Forward(artifact, "distributed_via", cloudFrontDistribution, distributedVia),
            Forward(buildPipeline, "source", sourceRepository, source),
            Inverse(buildPipeline, "produces", artifact, producedBy),
            Inverse(sourceRepository, "builds", buildPipeline, source),
            Forward(service, "depends_on", s3Bucket, dependsOnBucket),
            Forward(service, "depends_on", cloudFrontDistribution, dependsOnDistribution),
            Inverse(s3Bucket, "used_by", service, dependsOnBucket),
            Inverse(cloudFrontDistribution, "serves", service, dependsOnDistribution),
            Forward(cloudFrontDistribution, "origins", s3Bucket, origin),
            Inverse(s3Bucket, "served_by", cloudFrontDistribution, origin),
            Inverse(s3Bucket, "contains", artifact, storedIn),
            Forward(supplier, "depends_on_supplier", service, supplies, "Reads as 'the services that depend on this supplier'."),

            // Security.
            Forward(risk, "affects", product, affects),
            Forward(risk, "results_in", businessImpact, resultsIn),
            Inverse(businessImpact, "results_from", risk, resultsIn),
            Inverse(risk, "mitigated_by", controlAdoption, mitigates),
            Forward(controlAdoption, "addresses", risk, mitigates),
            Forward(businessImpact, "informs", controlAdoption, informs),
            Inverse(businessImpact, "informs", assumption, assumptionSupportsImpact, "The impact lists the assumption that supports it."),
            Forward(assumption, "supports", risk, assumptionSupportsRisk),
            Forward(assumption, "supports", businessImpact, assumptionSupportsImpact),
            Forward(controlAdoption, "control.specification", controlDefinition, derivedFrom, "The specification name resolves to the node id 'control-def:{specification}'; 'control.version' is a property."),
            Inverse(controlDefinition, "adopted_by", controlAdoption, derivedFrom),
            Forward(controlAdoption, "scope", buildPipeline, adoptionAppliesToPipeline),
            Forward(controlAdoption, "scope", artifact, adoptionAppliesToArtifact),
            Forward(controlAdoption, "scope", s3Bucket, adoptionAppliesToBucket),
            Forward(controlAdoption, "scope", cloudFrontDistribution, adoptionAppliesToDistribution),
            Forward(controlException, "scope", service, exceptionAppliesToService),
            Forward(controlException, "scope", s3Bucket, exceptionAppliesToBucket),
            Forward(controlException, "control_adoption", controlAdoption, exceptionTo),
            Forward(controlAdoption, "implemented_by", controlImplementation, adoptionImplementedBy),
            Inverse(controlImplementation, "implements", controlAdoption, adoptionImplementedBy),
            Inverse(controlImplementation, "validated_by", assertion, validatesImplementation),
            Forward(assertion, "supports", controlImplementation, validatesImplementation),
            Forward(assertion, "supports", controlAdoption, validatesAdoption),
            Inverse(observation, "assertion", assertion, producesObservations),
            Forward(observation, "subject.artifact", artifact, observes, "'subject.digest' is a property."),

            // Compliance.
            Forward(regulatoryFramework, "requirements", regulatoryRequirement, requires),
            Inverse(regulatoryRequirement, "framework", regulatoryFramework, requires),
            Forward(controlAdoption, "satisfies", regulatoryRequirement, satisfies),
            Inverse(regulatoryRequirement, "satisfied_by", controlAdoption, satisfies),
        ];

        UnmodeledRelationship[] unmodeledRelationships =
        [
            new(sourceRepository, "owner", "References 'team:platform-engineering'; teams are not node types in this slice."),
            new(service, "owned_by", "References 'team:platform-engineering'; teams are not node types in this slice."),
            new(controlAdoption, "owner", "References 'role:platform-security'; roles are not node types in this slice."),
            new(controlAdoption, "approver", "References 'role:CTO'; roles are not node types in this slice."),
            new(controlException, "owner", "References 'role:platform-security'; roles are not node types in this slice."),
            new(controlException, "approved_by", "References 'role:CTO'; roles are not node types in this slice."),
            new(cloudFrontDistribution, "declared_by", "References 'terraform:modules/release-distribution'; Terraform modules are not node types in this slice."),
            new(s3Bucket, "created_by", "References 'terraform:modules/release-storage'; Terraform modules are not node types in this slice."),
            new(cloudFrontDistribution, "observed_by", "References fixture documents under fixtures/aws/cloudfront/; fixture documents are not node types in this slice."),
            new(s3Bucket, "observed_by", "References fixture documents under fixtures/aws/s3/; fixture documents are not node types in this slice."),
            new(risk, "generic_risk", "References 'ARR-RISK-044' from a shared risk library that is not part of this slice (documented imperfection #6 in data/README.md)."),
            new(controlException, "control_requirement", "References ARR-3-11 clauses ('ARR-3-11.1', 'ARR-3-11.2'); clauses inside a control definition are below node granularity in this slice."),
            new(controlImplementation, "mechanisms.covers", "References ARR-3-11 clauses; same clause-level granularity as 'control_requirement'."),
            new(observation, "related_incident", "References 'INC-2026-041'; incidents are not node types in this slice."),
            new(supplier, "services_used", "Plain service names ('cloudfront', 's3', ...), not node references."),
            new(supplier, "external_assurance", "Document attachments (PDF reports); not graph nodes in this slice."),
            new(controlException, "compensating_controls", "Free-text mitigation items, not node references."),
        ];

        return new EdgeTypeRegistry(nodeTypes, edgeTypes, fieldMappings, unmodeledRelationships);
    }

    private static NodeType Node(string name, string idPrefix, string category, string description) =>
        new(name, idPrefix, category, description);

    private static EdgeType Edge(string forwardName, string inverseName, NodeType fromType, NodeType toType, string description) =>
        new(forwardName, inverseName, fromType, toType, description);

    private static RelationshipFieldMapping Forward(NodeType sourceType, string fieldPath, NodeType targetType, EdgeType edge, string? note = null) =>
        new(sourceType, fieldPath, targetType, edge, EdgeDirection.Forward, note);

    private static RelationshipFieldMapping Inverse(NodeType sourceType, string fieldPath, NodeType targetType, EdgeType edge, string? note = null) =>
        new(sourceType, fieldPath, targetType, edge, EdgeDirection.Inverse, note);
}
