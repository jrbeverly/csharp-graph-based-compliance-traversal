# Minimal Ontology and the Typed Edge Registry

The authoritative list of the node and edge types this slice supports, and how
the repository fixtures' relationship vocabulary maps onto it. The code
equivalent of this document is `EdgeTypeRegistry.Slice`
(`src/GraphBasedComplianceTraversal.Engine/Ontology/`), which is the contract
every later component compiles against. The test suite pins the code registry
to this document's content and to the fixtures in `data/**`.

## Node types

| Name | Id prefix | Category | Description |
|---|---|---|---|
| `BusinessPurpose` | `business-purpose` | Business | Why a set of systems exists: an organizational goal that products serve. |
| `Product` | `product` | Business | A customer-facing product the organization operates. |
| `ProductCapability` | `capability` | Business | A discrete capability a product provides. |
| `Service` | `service` | Technical | A system or service that realizes capabilities. |
| `SourceRepository` | `repository` | Technical | A source code repository. |
| `BuildPipeline` | `build-pipeline` | Technical | A CI/CD pipeline that builds and publishes artifacts. |
| `BuildRun` | `build-run` | Technical | A single CI workflow run of a build pipeline. |
| `Commit` | `commit` | Technical | A source commit in a repository. |
| `Artifact` | `artifact` | Technical | A built, signed, publishable software artifact. |
| `CloudFrontDistribution` | `aws:cloudfront` | Cloud | A CloudFront CDN distribution serving release artifacts. |
| `S3Bucket` | `aws:s3` | Cloud | An S3 bucket storing release artifacts. |
| `Supplier` | `supplier` | Supplier | An external supplier of infrastructure or services. |
| `Risk` | `risk` | Security | A risk of an undesirable outcome. |
| `BusinessImpact` | `impact` | Security | Why a risk matters to the organization. |
| `Assumption` | `assumption` | Security | An explicit assumption underlying assessments. |
| `ControlDefinition` | `control-def` | Security | An external, reusable control specification. |
| `ControlAdoption` | `control-adoption` | Security | The organization's adoption of a control definition. |
| `ControlImplementation` | `implementation` | Security | How an adopted control is realized. |
| `ControlException` | `exception` | Security | A scoped, time-bounded deviation from a control adoption. |
| `Assertion` | `assertion` | Security | A property expected to remain true, made testable. |
| `Observation` | `observation` | Security | A measured evaluation result of an assertion at a point in time. |
| `RegulatoryFramework` | `framework` | Compliance | A regulatory framework or directive. |
| `RegulatoryRequirement` | `requirement` | Compliance | A requirement within a regulatory framework. |

Every type except `Observation`, `BuildRun`, and `Commit` corresponds
one-to-one to a `type:` field in `data/**`. `Observation` is identified by the
`observation:` id prefix of the entries in
`data/security/observations/provenance-observations.yaml`, and `BuildRun` and
`Commit` are modeled by the external-source fixtures (the CI workflow-run
response and the commit response) rather than by `data/**` records.

## Edge types

Every edge type declares its endpoint node types, a forward name, and an
inverse name. The forward name reads from → to; the inverse name reads back.
Names are directional, and resolution is always disambiguated by the endpoint
node types — a few edge types therefore share a name across different endpoint
pairs, which is deliberate (e.g. `depends-on` from a service to an S3 bucket
versus a CloudFront distribution).

| Forward → inverse | From → To | Description |
|---|---|---|
| `fulfills` → `fulfilled-by` | Product → BusinessPurpose | A product exists to fulfill a business purpose. |
| `has-capability` → `belongs-to` | Product → ProductCapability | A product exposes a capability. |
| `implemented-by` → `implements` | ProductCapability → Service | A capability is realized by a service. |
| `implemented-by` → `implements` | ControlAdoption → ControlImplementation | An adopted control is realized by an implementation. |
| `produced-by` → `produces` | Artifact → BuildPipeline | A release artifact is produced by the build pipeline that created it. |
| `produced-by` → `produces` | Artifact → BuildRun | The build run that produced a release artifact. |
| `built-from` → `built-into` | BuildRun → Commit | A build run is built from the commit at its head. |
| `committed-to` → `has-commits` | Commit → SourceRepository | A commit was committed to a source repository. |
| `stored-in` → `contains` | Artifact → S3Bucket | A release artifact is stored in a bucket. |
| `distributed-via` → `distributes` | Artifact → CloudFrontDistribution | A release artifact reaches customers via a distribution. |
| `source` → `builds` | BuildPipeline → SourceRepository | A build pipeline builds from a source repository. |
| `depends-on` → `used-by` | Service → S3Bucket | A service depends on a bucket. |
| `depends-on` → `serves` | Service → CloudFrontDistribution | A service depends on a distribution. |
| `origin` → `served-by` | CloudFrontDistribution → S3Bucket | A distribution's origin is a bucket. |
| `supplies` → `supplied-by` | Supplier → Service | A supplier supplies (hosts or provides) a service. |
| `affects` → `affected-by` | Risk → Product | A risk affects a product. |
| `results-in` → `results-from` | Risk → BusinessImpact | A risk results in a business impact. |
| `mitigates` → `mitigated-by` | ControlAdoption → Risk | An adopted control mitigates a risk. |
| `informs` → `informed-by` | BusinessImpact → ControlAdoption | A business impact informs a control adoption decision. |
| `supports` → `supported-by` | Assumption → Risk | An assumption supports a risk assessment. |
| `supports` → `supported-by` | Assumption → BusinessImpact | An assumption supports a business impact assessment. |
| `derived-from` → `adopted-by` | ControlAdoption → ControlDefinition | An adoption derives from an external control definition. |
| `applies-to` → `in-scope-of` | ControlAdoption → BuildPipeline | An adopted control applies to a build pipeline. |
| `applies-to` → `in-scope-of` | ControlAdoption → Artifact | An adopted control applies to an artifact. |
| `applies-to` → `in-scope-of` | ControlAdoption → S3Bucket | An adopted control applies to a bucket. |
| `applies-to` → `in-scope-of` | ControlAdoption → CloudFrontDistribution | An adopted control applies to a distribution. |
| `applies-to` → `in-scope-of` | ControlException → Service | An exception applies to a service. |
| `applies-to` → `in-scope-of` | ControlException → S3Bucket | An exception applies to a bucket. |
| `validates` → `validated-by` | Assertion → ControlAdoption | An assertion makes an adopted control testable. |
| `validates` → `validated-by` | Assertion → ControlImplementation | An assertion validates an implementation. |
| `produces` → `produced-by` | Assertion → Observation | Evaluating an assertion produces observations. |
| `observes` → `observed-by` | Observation → Artifact | An observation measures an artifact. |
| `requires` → `belongs-to` | RegulatoryFramework → RegulatoryRequirement | A framework requires a requirement. |
| `satisfies` → `satisfied-by` | ControlAdoption → RegulatoryRequirement | An adopted control satisfies a regulatory requirement. |
| `exception-to` → `has-exception` | ControlException → ControlAdoption | An exception patches a control adoption. |

## Fixture relationship fields → canonical edge types

Every relationship field occurrence in `data/**` maps to exactly one canonical
edge type. An occurrence is identified by its source node type, its field path,
and the node type its value references — a single field may therefore appear
on several rows when its targets span multiple node types (`depends_on`,
`informs`, `scope`, `supports`). Direction: **forward** means the field reads
along the edge's forward name (from → to); **inverse** means it reads along the
inverse name (to → from).

| Source type | Field | Target type | Canonical | Direction | Note |
|---|---|---|---|---|---|
| BusinessPurpose | `fulfilled_by` | Product | `fulfilled-by` | inverse | |
| Product | `fulfills` | BusinessPurpose | `fulfills` | forward | |
| Product | `capabilities` | ProductCapability | `has-capability` | forward | |
| ProductCapability | `belongs_to` | Product | `belongs-to` | inverse | |
| ProductCapability | `implemented_by` | Service | `implemented-by` | forward | |
| Service | `implements` | ProductCapability | `implements` | inverse | |
| Artifact | `built_by` | BuildPipeline | `produced-by` | forward | Fixture synonym for `produced-by`. |
| Artifact | `stored_in` | S3Bucket | `stored-in` | forward | |
| Artifact | `distributed_via` | CloudFrontDistribution | `distributed-via` | forward | |
| BuildPipeline | `source` | SourceRepository | `source` | forward | |
| BuildPipeline | `produces` | Artifact | `produces` | inverse | |
| SourceRepository | `builds` | BuildPipeline | `builds` | inverse | |
| Service | `depends_on` | S3Bucket | `depends-on` | forward | |
| Service | `depends_on` | CloudFrontDistribution | `depends-on` | forward | |
| S3Bucket | `used_by` | Service | `used-by` | inverse | |
| CloudFrontDistribution | `serves` | Service | `serves` | inverse | |
| CloudFrontDistribution | `origins` | S3Bucket | `origin` | forward | |
| S3Bucket | `served_by` | CloudFrontDistribution | `served-by` | inverse | |
| S3Bucket | `contains` | Artifact | `contains` | inverse | |
| Supplier | `depends_on_supplier` | Service | `supplies` | forward | Reads as "the services that depend on this supplier". |
| Risk | `affects` | Product | `affects` | forward | |
| Risk | `results_in` | BusinessImpact | `results-in` | forward | |
| BusinessImpact | `results_from` | Risk | `results-from` | inverse | |
| Risk | `mitigated_by` | ControlAdoption | `mitigated-by` | inverse | |
| ControlAdoption | `addresses` | Risk | `mitigates` | forward | |
| BusinessImpact | `informs` | ControlAdoption | `informs` | forward | |
| BusinessImpact | `informs` | Assumption | `supported-by` | inverse | The impact lists the assumption that supports it. |
| Assumption | `supports` | Risk | `supports` | forward | |
| Assumption | `supports` | BusinessImpact | `supports` | forward | |
| ControlAdoption | `control.specification` | ControlDefinition | `derived-from` | forward | The specification name resolves to the node id `control-def:{specification}`; `control.version` is a property. |
| ControlDefinition | `adopted_by` | ControlAdoption | `adopted-by` | inverse | |
| ControlAdoption | `scope` | BuildPipeline | `applies-to` | forward | |
| ControlAdoption | `scope` | Artifact | `applies-to` | forward | |
| ControlAdoption | `scope` | S3Bucket | `applies-to` | forward | |
| ControlAdoption | `scope` | CloudFrontDistribution | `applies-to` | forward | |
| ControlException | `scope` | Service | `applies-to` | forward | |
| ControlException | `scope` | S3Bucket | `applies-to` | forward | |
| ControlException | `control_adoption` | ControlAdoption | `exception-to` | forward | |
| ControlAdoption | `implemented_by` | ControlImplementation | `implemented-by` | forward | |
| ControlImplementation | `implements` | ControlAdoption | `implements` | inverse | |
| ControlImplementation | `validated_by` | Assertion | `validated-by` | inverse | |
| Assertion | `supports` | ControlImplementation | `validates` | forward | |
| Assertion | `supports` | ControlAdoption | `validates` | forward | |
| Observation | `assertion` | Assertion | `produced-by` | inverse | |
| Observation | `subject.artifact` | Artifact | `observes` | forward | `subject.digest` is a property. |
| RegulatoryFramework | `requirements` | RegulatoryRequirement | `requires` | forward | |
| RegulatoryRequirement | `framework` | RegulatoryFramework | `belongs-to` | inverse | |
| ControlAdoption | `satisfies` | RegulatoryRequirement | `satisfies` | forward | |
| RegulatoryRequirement | `satisfied_by` | ControlAdoption | `satisfied-by` | inverse | |

## Intentionally unmodeled in this slice

Relationship fields present in `data/**` whose values reference entities
outside the declared node types, or carry free text rather than node
references. They are listed here explicitly so nothing is silently dropped.

| Source type | Field | Reason |
|---|---|---|
| SourceRepository | `owner` | References `team:platform-engineering`; teams are not node types in this slice. |
| Service | `owned_by` | References `team:platform-engineering`; teams are not node types in this slice. |
| ControlAdoption | `owner` | References `role:platform-security`; roles are not node types in this slice. |
| ControlAdoption | `approver` | References `role:CTO`; roles are not node types in this slice. |
| ControlException | `owner` | References `role:platform-security`; roles are not node types in this slice. |
| ControlException | `approved_by` | References `role:CTO`; roles are not node types in this slice. |
| CloudFrontDistribution | `declared_by` | References `terraform:modules/release-distribution`; Terraform modules are not node types in this slice. |
| S3Bucket | `created_by` | References `terraform:modules/release-storage`; Terraform modules are not node types in this slice. |
| CloudFrontDistribution | `observed_by` | References fixture documents under `fixtures/aws/cloudfront/`; fixture documents are not node types in this slice. |
| S3Bucket | `observed_by` | References fixture documents under `fixtures/aws/s3/`; fixture documents are not node types in this slice. |
| Risk | `generic_risk` | References `ARR-RISK-044` from a shared risk library that is not part of this slice (documented imperfection #6 in `data/README.md`). |
| ControlException | `control_requirement` | References ARR-3-11 clauses (`ARR-3-11.1`, `ARR-3-11.2`); clauses inside a control definition are below node granularity in this slice. |
| ControlImplementation | `mechanisms.covers` | References ARR-3-11 clauses; same clause-level granularity as `control_requirement`. |
| Observation | `related_incident` | References `INC-2026-041`; incidents are not node types in this slice. |
| Supplier | `services_used` | Plain service names (`cloudfront`, `s3`, …), not node references. |
| Supplier | `external_assurance` | Document attachments (PDF reports); not graph nodes in this slice. |
| ControlException | `compensating_controls` | Free-text mitigation items, not node references. |

## Validation semantics


`EdgeTypeRegistry.Resolve(name, fromType, toType)` resolves a candidate edge to
an edge type and a direction, and **rejects** it with an
`EdgeTypeViolationException` when:

- either endpoint node type is not declared by the slice, or
- no edge type declares `name` across exactly these endpoints in exactly this
  direction. Forward and inverse names are directional, so using a forward name
  with swapped endpoints is a rejection even though each endpoint is a valid
  node type.

The registry also validates its own definition at construction: duplicate node
type names or id prefixes, duplicate (name, endpoint) declarations, field
mappings inconsistent with their edge's declared endpoints, duplicate
(source type, field, target type) mappings, and fields that are both mapped
and listed as unmodeled are all rejected.

