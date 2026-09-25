# Cross-Domain Traversals and Explained Paths

The three canonical cross-domain paths the planning input names, each
demonstrated as a typed traversal over the shared graph and rendered as an
explained path — the ordered node sequence with the typed edge that connects
every pair of adjacent nodes named between them, so the path reads as its own
justification. Architecture, security, and risk are not independent
representations: they are different paths through one graph. The code
equivalent of this document is `CanonicalTraversals`, `ExplainedPath`, and
`TraversalReport` (`src/GraphBasedComplianceTraversal.Engine/Traversal/`),
built on the traversal query API (`docs/traversal.md`). The test suite pins
every path this document shows, golden-file by golden-file, against the
repository's own world — the declared `data/**` records merged with the
observed `fixtures/**` facts.

## The three canonical traversals

Each canonical traversal is a ready-built `GraphTraversal` query. The
infrastructure-to-business chain reads the inverse names over the same stored
edges the business-to-infrastructure chain reads forward — no edge is defined
twice.

### Resource → purpose

Why a resource exists, from the bucket up through the service that uses it,
the capability the service implements, the product the capability belongs to,
and the business purpose the product fulfills:

```text
aws:s3:prod-release-artifacts
  → used-by → service:release-distribution [depends-on, inverse]
  → implements → capability:software-update-delivery [implemented-by, inverse]
  → belongs-to → product:customer-agent [has-capability, inverse]
  → fulfills → business-purpose:secure-endpoint-management [fulfills, forward]
```

### Artifact → source/provenance

What an artifact is and where it came from. The declared build chain reaches
its build pipeline, the source repository that pipeline builds from, and the
source commit in it:

```text
artifact:customer-agent-2.8.4
  → produced-by → build-pipeline:customer-agent-release [produced-by, forward]
  → source → repository:customer-agent [source, forward]
  → has-commits → commit:7a94f3e8b2c1d5f6a7b8c9d0e1f2a3b4c5d6e7f8 [committed-to, inverse]
```

The attested derivation chain reaches the build run the provenance
attestation binds the artifact to, that run's source commit, and its
repository — the `produced-by` step's edge carries the attestation's
provenance record (`fixtures/provenance/sigstore-attestation.json`,
`slsa-provenance/v1`) alongside the CI record naming the run, so the path
names the provenance information that exists for the artifact:

```text
artifact:customer-agent-2.8.4
  → produced-by → build-run:84125 [produced-by, forward]
  → built-from → commit:7a94f3e8b2c1d5f6a7b8c9d0e1f2a3b4c5d6e7f8 [built-from, forward]
  → committed-to → repository:customer-agent [committed-to, forward]
```

And where it is distributed:

```text
artifact:customer-agent-2.8.4
  → distributed-via → aws:cloudfront:customer-downloads [distributed-via, forward]
```

### Risk → control

Why a risk matters and how the organization controls it, through the business
impact it results in, the control adoption that impact informed, the
implementation realizing the adoption, and the assertion validating it —
fanning out into one path per observation the assertion produced, in
observation order:

```text
risk:artifact-tampering
  → results-in → impact:artifact-compromise [results-in, forward]
  → informs → control-adoption:ca-artifact-supply-chain [informs, forward]
  → implemented-by → implementation:signed-release-pipeline [implemented-by, forward]
  → validated-by → assertion:prod-artifact-provenance [validates, inverse]
  → produces → observation:obs-2026-08-08-001 [produces, forward]  (and the four older ones)
```

## The reverse: which technical systems realize a capability

The same edges read the other way. From a business capability,
`implemented-by` enumerates the technical systems that realize it:

```text
capability:software-update-delivery
  → implemented-by → service:release-distribution [implemented-by, forward]
```

From the business purpose, the full reverse chain fans out over the service's
two technical dependencies — one path ending at the CloudFront distribution,
one at the S3 bucket:

```text
business-purpose:secure-endpoint-management
  → fulfilled-by → product:customer-agent [fulfills, inverse]
  → has-capability → capability:software-update-delivery [has-capability, forward]
  → implemented-by → service:release-distribution [implemented-by, forward]
  → depends-on → aws:cloudfront:customer-downloads [depends-on, forward]  (and the same chain ending at aws:s3:prod-release-artifacts)
```

## The rendering

`ExplainedPath.Render` writes a path as the start node followed by one line
per step: the queried edge name, the reached node, the traversed edge type's
forward name, and the direction the step moved in. Every pair of adjacent
nodes is therefore joined by a named typed edge — the path reads as a
justification, not an assertion. Rendering is deterministic and never
invents a hop: a cycle is rendered as the repeated node, exactly as the
traversal reported it.

## Unresolved references are reported, not silently skipped

A traversal may hit an edge whose reached endpoint has no stored node — the
declared world's product lists capabilities without records. The walk cannot
step onto a missing node, so the route ends before that edge; without a
report, the incomplete walk reads as a complete one that simply matched
nothing. `GraphTraversal.ExecuteWithGaps` returns a `TraversalReport` carrying
the completed paths alongside every such gap, and `ExplainedPath.Render`
renders the gaps after the paths:

```text
Unresolved references:
  has-capability → capability:endpoint-telemetry-collection (from product:customer-agent)
  has-capability → capability:policy-enforcement (from product:customer-agent)
```

The reverse chain above hits exactly these two gaps and reports them while
still returning its two completed paths. A traversal whose every route ends
at an unresolved reference reports the gaps and no paths — never silence.

## Out of this slice

- **Judging whether observations are current or passing** — the Assertions
  milestone.
- **Prose narrative and multi-view projection** — the final milestone; the
  explained path is the structured typed-edge sequence those renderers
  consume.
- **Freshness of the demonstration world** — the canonical traversals run
  over whatever the loaders feed the shared graph.

