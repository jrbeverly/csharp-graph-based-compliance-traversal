# Multi-View Projections and Compliance as a Derived View

Architecture, security, risk, and compliance are not independent
representations: they are different projections of one shared graph. A view
is a projection function over the shared nodes — it selects the nodes and
typed paths that constitute one domain's perspective and references the
graph's own `GraphNode` instances, never copying facts into a view-owned
store, so no view can drift from the graph or from the other views built
over it. In particular, the compliance view is a derived view: it is
produced entirely by traversing from a framework requirement through the
organization's actual risk and control model to the current observations —
never from a parallel compliance-specific database. The code equivalent of
this document is `DomainViews`, `GraphView`, `ViewObservation`, and
`ViewRendering` (`src/GraphBasedComplianceTraversal.Engine/Views/`), the
canonical assurance grammar `CanonicalPathGrammars.SupplyChainAssurance`
(`src/GraphBasedComplianceTraversal.Engine/Traversal/`), built on the path
grammars (`docs/path-grammars.md`), the canonical traversals
(`docs/explained-paths.md`), and the observation store
(`docs/observation-store.md`). The test suite pins every view this document
shows, golden-file by golden-file, against the repository's own world — the
declared `data/**` records merged with the observed `fixtures/**` facts.

## The three named views

Each view is one call over the shared graph:

| View         | Entry node                        | Chain                                             |
| ------------ | --------------------------------- | ------------------------------------------------- |
| architecture | `service:release-distribution`    | service → distribution → artifact → build → repository |
| security     | `risk:artifact-tampering`         | risk → impact → adoption → implementation → assertion → observation |
| compliance   | `requirement:nis2-article-21-supply-chain` | requirement → adoption → implementation → assertion → observation |

### Architecture

The release architecture in one typed walk from the service that distributes
the Customer Agent, through the distribution it depends on, the artifact
distributed through it, the build pipeline that produced the artifact, to
the source repository that build draws from:

```text
service:release-distribution
  → depends-on → aws:cloudfront:customer-downloads [depends-on, forward]
  → distributes → artifact:customer-agent-2.8.4 [distributed-via, inverse]
  → produced-by → build-pipeline:customer-agent-release [produced-by, forward]
  → source → repository:customer-agent [source, forward]
```

Every hop is a stored edge of the shared graph, so the view covers the
service, repository, build, artifact, and distribution the planning input
names without declaring any of them twice.

### Security

Why the artifact-tampering risk matters and how the organization controls
it, through the business impact it results in, the control adoption that
impact informed, the implementation realizing the adoption, and the
assertion validating it — fanning out into one path per observation the
assertion produced, in observation order:

```text
risk:artifact-tampering
  → results-in → impact:artifact-compromise [results-in, forward]
  → informs → control-adoption:ca-artifact-supply-chain [informs, forward]
  → implemented-by → implementation:signed-release-pipeline [implemented-by, forward]
  → validated-by → assertion:prod-artifact-provenance [validates, inverse]
  → produces → observation:obs-2026-08-08-001 [produces, forward]  (and the four older ones)
```

### Compliance

The compliance view derives the NIS2 supply-chain requirement by traversal.
The requirement is the entry point: the shared supply-chain assurance
grammar is evaluated from it, so the view reaches the control adoption, the
implementation, the assertion, and the observations through the same typed
edge sequence the path-grammar milestone declared — the control and
infrastructure descriptions are *referenced*, never restated:

```text
requirement:nis2-article-21-supply-chain
  → satisfied-by → control-adoption:ca-artifact-supply-chain [satisfies, inverse]
  → implemented-by → implementation:signed-release-pipeline [implemented-by, forward]
  → validated-by → assertion:prod-artifact-provenance [validates, inverse]
  → produces → observation:obs-2026-08-08-001 [produces, forward]  (and the four older ones)
```

When no valid assurance path exists, the view carries no paths and no
observation state — the grammar's explicit `NoValidPath` verdict is
projected as an empty view, never as a fabricated satisfaction.

## Views reference the shared nodes

`GraphView.Nodes` returns the distinct nodes the view's paths touch, in
first-seen order — the graph's own instances. A node shared between two
views is the same object the graph stores: the security and compliance
views share the control adoption, the implementation, the assertion, and
all five observations, and every node any view projects is
reference-equal to `TypedPropertyGraph.GetNode(id)`. The view type stores
no properties of its own — there is nowhere for a copy to live.

## The compliance view carries the current observation state

`DomainViews.Compliance(graph, store)` annotates every observation node its
valid paths reach with the observation store's current state — the state
the store surfaces and the state it was recorded with, side by side, the
way `docs/observation-store.md` defines them:

```text
Current observation state:
  observation:obs-2026-08-08-001 pass
  observation:obs-2026-08-07-001 pass
  observation:obs-2026-08-06-001 fail
  observation:obs-2026-08-05-001 stale (recorded pass, validator provenance-prober@3.1.6)
  observation:obs-2026-07-15-001 stale (recorded pass, validator provenance-prober@3.1.5)
```

The seeded signals stay visible: the recorded `FAIL` of the blocked
unsigned upload stays a failure, and the two observations produced by
validators behind the assertion's current validator are surfaced `STALE`
while their recorded passes remain queryable. The view never aggregates
these states into a blanket success — a requirement whose observations
include failures and outdated evidence reads as carrying failures and
outdated evidence.

## The rendering

`ViewRendering.Render` writes a view as the name and subject line, the
explained paths (reusing `ExplainedPath`, so every hop names the typed edge
that justifies it), and the current observation state when the view carries
any. The rendering is deterministic and is the structured format the
golden files pin; prose narrative rendering is a later milestone.

## Out of this slice

- **Prose narrative rendering and the AI boundary** — turning the views
  into narrative documents and deciding where a language model may or may
  not contribute is the next issue.
- **Additional frameworks** — the compliance view is demonstrated over the
  fixtures' single NIS2 requirement; further frameworks are out of scope.
- **Serialized view catalogs** — the views are constructed in code; a
  registry-curated catalog is not part of this slice.


