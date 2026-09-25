# Path Grammars and Semantic Path Constraints

The layer that constrains which edge sequences constitute a valid conclusion.
Raw traversal (`docs/traversal.md`) executes any sequence of declared steps; a
path grammar constrains the sequence itself, so an "addressed"/"satisfied"
answer always corresponds to a real, semantically valid edge sequence — the
system claims *how and why* two things relate, not merely that some path
exists. The code equivalent of this document is `PathGrammar`,
`PathGrammarStep`, and `PathGrammarResult`
(`src/GraphBasedComplianceTraversal.Engine/Traversal/`), built on the graph's
stored edges (`docs/graph.md`). The test suite pins this document's behavior
against the repository's own declared world.

## The grammar

A grammar is a name plus an ordered chain of steps; each step is an **edge
type traversed in a direction** — never merely a name, so edge types that
share a name across different endpoint pairs (the two `implemented-by` edge
types, for example) stay distinct. Because each edge type declares its
endpoint node types, the chain also pins the node type at every position: a
grammar's steps connect across matching node types by construction.

Steps may be declared two equivalent ways:

```csharp
// By edge name with the node types it connects — the slice resolves the
// name across the endpoints to one declared edge type and direction.
var assurancePath = new PathGrammar(EdgeTypeRegistry.Slice, "supply-chain-assurance")
    .Step("satisfied-by", requirement, controlAdoption)
    .Step("implemented-by", controlAdoption, controlImplementation)
    .Step("validated-by", controlImplementation, assertion)
    .Step("produces", assertion, observation);
```

The same grammar declared directly by edge type and direction:

```csharp
var assurancePath = new PathGrammar(EdgeTypeRegistry.Slice, "supply-chain-assurance")
    .Step(satisfies, EdgeDirection.Inverse)                 // reads satisfied-by
    .Step(adoptionImplementedBy, EdgeDirection.Forward)     // reads implemented-by
    .Step(validatesImplementation, EdgeDirection.Inverse)   // reads validated-by
    .Step(producesObservations, EdgeDirection.Forward);     // reads produces
```

This is the supply-chain assurance path of the issue:

```text
requirement → (satisfied-by) control-adoption → (implemented-by)
implementation → (validated-by) assertion → (produces) observation
```

A grammar is type-checked as it is built, like a traversal query: an edge
type the ontology slice does not declare, a name the slice does not resolve
across the given endpoint types, or a step that does not chain onto the
preceding step's exit node type is rejected when the step is added — a
mistyped grammar never silently matches nothing.

## Evaluation

```csharp
var result = assurancePath.Evaluate(graph, "requirement:nis2-article-21-supply-chain");
```

`Evaluate` returns a `PathGrammarResult` carrying the explicit verdict and
both sides of the evidence:

- `ValidPaths` — every path from the start node that followed the grammar's
  permitted edge sequence, in deterministic step-by-step fan-out order. A
  requirement satisfiable by more than one valid path returns them **all**;
  multiple observations one assertion produced fan out into one valid path
  each. A step onto a node already on the path ends that route — a loop is
  not a valid assurance chain.
- `RejectedPaths` — every simple path from the same start that reached a node
  of the grammar's terminal node type through a **different** edge sequence,
  in deterministic depth-first order over the graph's insertion order. The
  candidate space is bounded by the grammar's own length: a path longer than
  the grammar cannot match it, so nothing beyond it is examined. Reporting
  the rejections keeps a disallowed conclusion visible as evidence — the
  grammar rejects it rather than swallowing it.
- `Verdict` / `HasValidPath` — `ValidPath` when at least one path matched;
  otherwise an explicit `NoValidPath` verdict, never a fabricated path —
  whether no route reached the terminal node type at all, or every route that
  did was through a disallowed sequence.

A start node that is not in the graph is rejected with a
`KeyNotFoundException`, exactly like a traversal. A start node whose type is
not the grammar's start type is not an error: the grammar does not govern
that node, so the verdict is `NoValidPath`.

`Matches(path)` answers whether one given path follows the grammar: the same
number of steps, each traversing the grammar's step edge type in the
grammar's step direction, with no step onto a repeated node. The node types
along the path follow from the edge types, so a matching path is a
semantically valid edge sequence in its entirety.

## The declared world

The assurance grammar above, evaluated from
`requirement:nis2-article-21-supply-chain`, finds the five valid paths the
declared world intends — the full
`requirement → control-adoption → implementation → assertion` chain, one per
observation the assertion produced:

```text
requirement:nis2-article-21-supply-chain
  → satisfied-by → control-adoption:ca-artifact-supply-chain
  → implemented-by → implementation:signed-release-pipeline
  → validated-by → assertion:prod-artifact-provenance
  → produces → observation:obs-2026-08-08-001   (and the four older ones)
```

And it rejects the nine same-terminal routes built from semantically wrong
edges — the adoption validated by the assertion directly (skipping the
implementation hop, one per observation), and the scope shortcuts around the
assertion entirely (`applies-to` to the pipeline, artifact, bucket, or
distribution, then to the observed artifact and its observation). None of
them is ever reported as satisfaction; all of them are surfaced as
rejections.

## Out of this slice

- **Cross-domain demonstrations** — applying grammars to concrete
  demonstrations beyond the supply-chain assurance path is the next issue.
- **Freshness judgment** — assertion state and observations feeding a
  grammar's freshness judgment are consumed later in reporting.
- **Declared grammars** — grammars are constructed in code; a serialized or
  registry-curated grammar catalog is not part of this slice.

