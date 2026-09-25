# Explainable Narratives and the AI-Downstream Boundary

A compliance conclusion is rendered as an explanation of a verified graph
path: a narrative compiler turns a validated traversal — or a grammar result
carrying an explicit no-valid-path verdict — into a structured explanation,
and a deterministic, AI-free default renderer produces the human-readable
document from that structure. An optional AI renderer may attach downstream
of the structured facts, where it can verbalize an established path but has
no surface through which to invent a missing edge. The code equivalent of
this document is `NarrativeCompiler`, `CompiledNarrative`, `NarrativeStep`,
`NarrativeStatement`, `NarrativeFact`, `NarrativeGap`,
`DefaultNarrativeRenderer`, and the `INarrativeRenderer` boundary
(`src/GraphBasedComplianceTraversal.Engine/Narrative/`), built on the
canonical path grammars (`docs/path-grammars.md`), the views
(`docs/views.md`), the explained paths (`docs/explained-paths.md`), and the
per-fact provenance (`docs/provenance.md`). The test suite pins the
narrative's rendering, golden-file by golden-file, against the repository's
own world — the declared `data/**` records merged with the observed
`fixtures/**` facts.

## The pipeline

```text
validated path  ──┐
                  ├──► NarrativeCompiler ──► CompiledNarrative ──► renderer ──► document
no-valid-path ───┘                                    ▲
                                                      │
                                      DefaultNarrativeRenderer (deterministic, no AI)
                                      or an optional INarrativeRenderer (AI) at the same boundary
```

The compiler is the last stage that sees the graph. Everything downstream
receives only the compiled structure.

## The compiled structure

`CompiledNarrative` is the structured explanation, immutable data throughout:

- **Steps** — every distinct step of the validated paths, in first-seen
  order. A step carries the two node ids it connects, the queried edge name,
  the traversed edge type's forward name, the direction, and its supporting
  facts. A step shared by several paths — the same stored edge between the
  same nodes — compiles once: it is the same established fact, stated once.
- **Statements** — the narrative's sentences, in order. Every sentence maps
  to the step(s) it was templated from and the facts it rests on; a verdict
  sentence maps to the subject's facts instead. No sentence maps to nothing.
- **Facts** — the distinct supporting facts in first-seen order, each with
  its provenance: the edge traversed, each node's existence, the `name`
  properties the sentences read, and — on observation steps — the recorded
  `result`, `observed_at`, and `validator_version`, plus the observation
  store's current surface when it differs from the recorded state.
- **Verdict** — `Established` when validated paths were compiled, `Gap`
  when none existed. The verdict travels with the structure.

## The supply-chain compliance conclusion

`DomainNarratives.SupplyChainCompliance(graph, store)` compiles the
demonstrated conclusion: the NIS2 supply-chain requirement as the entry
point, the shared supply-chain assurance grammar evaluated from it — the
same derivation the compliance view performs — with the observation store's
current state attached to the observation steps. The narrative reads as its
own justification:

```text
Supply Chain Assurance: "NIS2 Article 21 — Supply Chain Security" (requirement:nis2-article-21-supply-chain)

"NIS2 Article 21 — Supply Chain Security" (requirement:nis2-article-21-supply-chain) is satisfied by "Adoption of ARR-3-11 Secure Software Artifact Supply Chain" (control-adoption:ca-artifact-supply-chain).
"Adoption of ARR-3-11 Secure Software Artifact Supply Chain" (control-adoption:ca-artifact-supply-chain) is implemented by "Signed Release Pipeline" (implementation:signed-release-pipeline).
"Signed Release Pipeline" (implementation:signed-release-pipeline) is validated by "Production artifact provenance validation" (assertion:prod-artifact-provenance).
"Production artifact provenance validation" (assertion:prod-artifact-provenance) produces observation:obs-2026-08-06-001, recorded fail at 2026-08-06T23:42:11Z by validator provenance-prober@3.1.7.
...
The supply chain assurance path from "NIS2 Article 21 — Supply Chain Security" (requirement:nis2-article-21-supply-chain) is established: 5 validated paths, 8 distinct steps, every hop a stored typed edge of the shared graph.
```

Every sentence is templated from a step: the queried edge name resolves
through the compiler's phrase table to the verb phrase the sentence reads
the edge as (`satisfied-by` → *is satisfied by*), and the sentence names the
nodes the step connects. The recorded `FAIL` of the blocked unsigned upload
stays a failure, and the two observations produced by outdated validators
state the store's surface — *the store currently surfaces stale (recorded
pass)* — rather than being smoothed into current evidence.

The rendered document carries the traceability inline: after the sentences,
a *Supporting facts* section lists every fact a sentence maps to, with its
sources:

```text
Supporting facts:
  [edge] satisfied-by: requirement:nis2-article-21-supply-chain → control-adoption:ca-artifact-supply-chain
    source: declaration data/security/controls/adoptions/ca-artifact-supply-chain.yaml
    source: declaration data/compliance/requirements/nis2-article-21-supply-chain.yaml
  [property] control-adoption:ca-artifact-supply-chain/name = "Adoption of ARR-3-11 Secure Software Artifact Supply Chain"
    source: declaration data/security/controls/adoptions/ca-artifact-supply-chain.yaml
```

A fact with no recorded source renders `source: unknown` — explicitly
unknown, never omitted and never dressed up as authoritative.

## A missing path is stated, never manufactured

When the grammar returns its explicit no-valid-path verdict, the compiler
replays the grammar's steps from the start — the same walk `Evaluate`
performs — to find the first step at which the frontier goes empty, and
compiles one gap per frontier node. The narrative states the break and draws
no conclusion:

```text
The chain breaks at the required step 'satisfied-by': requirement:nis2-article-21-supply-chain has no stored 'satisfied-by' edge reaching any ControlAdoption node.
The supply chain assurance path from requirement:nis2-article-21-supply-chain does not exist: no conclusion is drawn, and nothing is inferred from the absence of a path.
```

Routes that reached the grammar's terminal type through a different edge
sequence are reported as rejected evidence when present; they exist, and
they do not satisfy the grammar.

## The default rendering is deterministic and requires no AI

`NarrativeRendering.Render` assembles the document from the compiled
structure — the title, the sentences in order, the supporting facts — by
pure templating: no model, no randomness, no clock. The same compiled
narrative always renders the same text, which the test suite pins
golden-file by golden-file (`tests/.../Golden/narrative/`), regenerated only
through `make test-update-goldens`. `DefaultNarrativeRenderer` is that
rendering as an ordinary `INarrativeRenderer` implementation, so the
demonstration never depends on an AI renderer existing.

## The AI boundary

`INarrativeRenderer` is the single downstream entry point any renderer
implements — the deterministic default, or an optional AI renderer attached
at the same place:

```csharp
public interface INarrativeRenderer
{
    string Render(CompiledNarrative narrative);
}
```

The governing rule, enforced by what the boundary passes and does not pass:

> **AI may verbalize an established path. AI may not invent a missing edge.**

The boundary is enforced in four ways:

1. **A renderer receives only the compiled structure.** `Render` takes a
   `CompiledNarrative` and nothing else — no graph, no store, no traversal
   engine. There is no reference through which a renderer could add an edge
   to the graph or read facts the narrative did not compile, and the graph's
   own mutation surface (`GraphNode.SetProperty`, `GraphEdge` provenance
   appends, graph insertion) is internal to the engine.
2. **The compiled structure is immutable.** `CompiledNarrative`,
   `NarrativeStep`, `NarrativeStatement`, `NarrativeFact`, and `NarrativeGap`
   expose only getters; `FactProvenance` and `ProvenanceRecord` are immutable
   too. A renderer can read the established steps and facts, never alter
   them.
3. **The verdict travels with the structure.** A gap narrative carries its
   gaps and the verdict `Gap`; the contract of the interface (and the
   deterministic default renderer) preserves it. A renderer must not render
   a gap narrative as a conclusion, and it must not render text
   contradicting the statements it was given — it may rephrase them, and the
   rephrasing must remain traceable to the same steps and facts the compiled
   statement maps to.
4. **The default renderer is deterministic and AI-free.** The demonstration
   path never requires a model; an AI renderer is an optional addition at
   the boundary, not a dependency of the pipeline.

In short: prose is a rendering concern. Facts, edges, and conclusions are
compiled upstream, from the graph, with provenance — and downstream of the
compiler, nothing may add to them.

## Out of this slice

- **Implementing an actual AI/LLM renderer** — the boundary is defined,
  documented, and tested; the renderer itself is deliberately not part of
  this slice.
- **Reproducible-report metadata and the full acceptance walkthrough** —
  graph snapshots, compiler versions, and the end-to-end demonstration are
  the final issue.

