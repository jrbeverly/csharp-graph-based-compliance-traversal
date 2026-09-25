# Assertion Evaluation and Produced Observations

The mechanism for evaluating assertions — properties the organization expects
to remain true — against the graph. Evaluating an assertion produces one
observation per subject, carrying a state from the shared validation model,
the evaluation timestamp, and the provenance of the facts consulted. The code
equivalent of this document is `Assertion`, `AssertionObservation`,
`AssertionEvaluation`, and `AssertionRegistry`
(`src/GraphBasedComplianceTraversal.Engine/Assertions/`), the Assertions
milestone the state model, graph, and provenance slices pointed at. The test
suite pins this document's behavior against the repository's own `data/**`
world.

## The assertion abstraction

An `Assertion` declares a property, it does not demonstrate it:

| Member | Meaning |
|---|---|
| `Id` | The stable identifier, shared with the `Assertion` node the record under `data/security/assertions/**` carries (for example `assertion:prod-artifact-provenance`). |
| `Statement` | The invariant in one sentence: "Every production artifact has valid provenance." |
| `Selector` | An `IAssertionSubjectSelector` — the class/type or graph query deciding which graph nodes the property applies to. |
| `Predicate` | An `IAssertionPredicate` — decides, per subject, whether the graph's facts satisfy the invariant. |

The two built-in selector shapes are `NodeTypeSubjectSelector` (every node of
a declared type, optionally narrowed by a filter — "every production
artifact") and `QuerySubjectSelector` (an arbitrary graph query — "every
production resource", which spans the cloud resource node types). Selectors
order subjects by graph insertion order, so evaluation is deterministic.

## Evaluation rules

`Assertion.Evaluate(graph, evaluatedAt)` selects the subjects, applies the
predicate to each, and produces one `AssertionObservation` per subject:

```text
AssertionObservation:
  assertion:  assertion:prod-artifact-provenance
  subject:    artifact:customer-agent-2.8.4
  state:      PASS
  evaluated:  2026-08-12T09:00:00Z
  provenance: data/technical/artifacts/customer-agent-2.8.4.yaml
  reason:     The artifact declares a complete signing statement: ...
```

Evaluation is deterministic: states are decided from the graph's facts alone,
observations appear in assertion-then-selector order, and the evaluator never
reads the clock — the `evaluatedAt` timestamp is a parameter shared by every
observation of an evaluation run.

Every decision is honest:

| Facts | State |
|---|---|
| The subject's facts satisfy the invariant. | `PASS` |
| The subject's facts violate it (an incomplete signing statement, an owner declared but empty, an implementation declared with no record). | `FAIL` |
| The subject carries no fact bearing on the property. | `UNKNOWN` |

The last row is the epistemic rule carried over from the state model: absence
of a supporting fact is never an implied pass. A subject with no supporting
fact yields `UNKNOWN`, no matter how plausible the property looks.

## The slice's assertions

`AssertionRegistry.Slice` carries the three assertions the fixtures imply:

| Id | Statement | Subjects | Predicate |
|---|---|---|---|
| `assertion:prod-artifact-provenance` | Every production artifact has valid provenance. | `Artifact` nodes classified production | The `signing` property must be a structured statement with non-empty `method`, `identity`, and `transparency_log`. This is the compiled form of the declared `Assertion` record, whose `subject.class` and `predicate.valid_provenance` fields it realizes. |
| `assertion:production-resource-owner` | Every production resource has an owner. | Cloud-category nodes classified production | A non-empty `owner` or `owned_by` property passes; one declared but empty fails; no owner fact yields `UNKNOWN`. |
| `assertion:adopted-control-implementation` | Every adopted control has an implementation. | `ControlAdoption` nodes | A resolved `implemented-by` edge passes; an edge naming an implementation with no record fails; no edge yields `UNKNOWN`. |

Over the loaded declared graph the slice evaluates to four observations:

```text
assertion:prod-artifact-provenance     artifact:customer-agent-2.8.4        PASS
assertion:production-resource-owner    aws:cloudfront:customer-downloads    UNKNOWN
assertion:production-resource-owner    aws:s3:prod-release-artifacts        UNKNOWN
assertion:adopted-control-implementation control-adoption:ca-artifact-supply-chain PASS
```

The two `UNKNOWN` states are the fixture world's deliberate imperfection: the
production resources declare no owner, and the evaluation says so instead of
passing them by omission.

## Provenance

Each produced observation records the provenance of the facts the predicate
consulted — evidence of the evaluation, not just of the outcome. A `PASS` or
`FAIL` carries the provenance of the deciding facts (the `signing` property's
records, the owner property's records, the `implemented-by` edge's records);
an `UNKNOWN` carries the provenance of the subject's fact set that was
consulted and found to carry no supporting fact. A fact with no known source
is marked `FactProvenance.None` — the observation then honestly reports that
the facts it consulted have no recorded origin.

## Out of this slice

- **The observation store and the recorded history** — the next milestone,
  implemented in [docs/observation-store.md](observation-store.md). The
  observations under `data/security/observations/**` are ingested into the
  store and surfaced with the staleness signal; folding them into evaluation
  (freshness, history-based states) is separate work.
- **Structural linting and graph-health scoring** — the linter issue.
  Age-based `STALE`, `EXPIRED`, `EXCEPTION`, and the rest of the shared
  `ValidationState` enumeration are decided by specific invariants this slice
  does not add.
- **Writing observations back into the graph** — evaluation produces
  `AssertionObservation` records; persisting them as `Observation` nodes is a
  later milestone's concern.
