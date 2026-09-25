# Organizational Linting and Graph-Health Findings

The linter that treats incomplete or invalid organizational structure the way
a compiler treats a type error: it walks the reconciled graph and the
observation store and reports structural gaps and unhealthy state as findings.
Missing information is never read as success — a subject whose supporting
facts are merely absent yields a finding, never a silent pass. The code
equivalent of this document is `OrganizationalLinter` and `LintFinding`
(`src/GraphBasedComplianceTraversal.Engine/Linting/`), the Linter milestone
the state model, graph, ingestion, reconciliation, and observation-store
slices pointed at. The test suite pins this document's behavior against the
repository's own world (the full 45-finding run is golden-pinned under
`tests/GraphBasedComplianceTraversal.Tests/Golden/linter-findings.txt`) and
against synthetic graphs for the negative paths.

## The finding

`OrganizationalLinter.Lint(graph, store, referenceTimestamp)` walks the
reconciled graph — the declared facts, the adapters' observed facts, and the
reconciliation verdicts — together with the observation store's history, and
returns an ordered list of `LintFinding` records:

| Member | Meaning |
|---|---|
| `SubjectId` | The stable id of the graph subject the finding is about — or the referenced id that has no record. |
| `Invariant` | The failed invariant, from the linter's fixed check list below. |
| `State` | The resulting state from the shared `ValidationState` model — `FAIL`, `STALE`, `EXPIRED`, `EXCEPTION`, `CONFLICTING`, or `UNKNOWN`. A check that passes produces **no finding at all**, and `LintFinding` refuses to be constructed with `PASS`. |
| `Reason` | What the invariant expected and what is missing or unhealthy. |

Findings are deterministic: the checks run in a fixed order, subjects are
visited in graph insertion order and store timeline order, and the reference
timestamp is a parameter — the linter never reads the clock, so the same
inputs always produce equal findings.

## The checks

The structural checks come from the planning input; the graph-health checks
run over the ingested world.

### Structural checks

| Invariant | Subject | Rule |
|---|---|---|
| `production-resource-owner` | Cloud-category nodes classified `production` | A non-empty `owner`/`owned_by` property passes; one declared but empty is `FAIL`; no owner fact is `UNKNOWN` — a production resource with no recorded owner is structurally invalid. |
| `risk-treatment` | `Risk` nodes | A resolved `mitigated-by` edge to a control adoption passes; an edge naming an adoption with no record is `FAIL`; no treatment fact is `UNKNOWN`. |
| `adoption-implementation` | `ControlAdoption` nodes | A resolved `implemented-by` edge passes; a missing implementation record is `FAIL`; no implementation fact is `UNKNOWN`. |
| `implementation-assertion` | `ControlImplementation` nodes | A resolved `validated-by` edge to an assertion passes; a missing assertion record is `FAIL`; no validating assertion fact is `UNKNOWN`. |
| `artifact-source-path` | `Artifact` nodes | An artifact whose derivation facts reach a `SourceRepository` through any typed edge chain passes; one whose derivation chain does not reach a repository is `FAIL`; one with no `produced-by` fact at all is `UNKNOWN`. |

### Graph-health checks

| Invariant | Subject | Rule |
|---|---|---|
| `unresolved-reference` | The missing endpoint id | Every typed edge whose target has no record is a finding: the relationship is declared, its target is not part of the ingested world. |
| `unmodeled-reference` | The referenced id | Every reference through a relationship field the ontology declares unmodeled — owners pointing at `team:`/`role:` ids, the shared risk library's `ARR-RISK-044`, control-definition clauses, incidents — is a finding: it is recorded, never traversable. |
| `exception-expiry` | `ControlException` nodes | An expiry in the past is `EXPIRED`; an expiry within 30 days of the reference date is flagged `EXCEPTION` (still covered, expiring imminently); no expiry date is `UNKNOWN`. |
| `supplier-assessment-freshness` | `Supplier` nodes | A `last_risk_assessment` older than 60 days before the reference date is `STALE`; no assessment date is `UNKNOWN`. |
| `requirement-supplier-mapping` | `RegulatoryRequirement` nodes | A requirement whose satisfaction chain reaches a supplier but declares no mapping to that supplier is `UNKNOWN`; the check does not apply to requirements with no supplier in their satisfaction domain. |
| `declared-observed-reconciliation` | The reconciled subject | Every reconciliation the graph records `CONFLICTING` is a finding carrying both values and the source-authority precedence — surfaced, never silently resolved. Corroboration is agreement, not a gap. |
| `observation-result` | The observed subject | Every stored observation recorded `FAIL` is a finding, naming its observation id and related incident. |
| `observation-validator-staleness` | The observed subject | Every stored observation produced by a validator version behind its assertion's current validator is `STALE`. |

## The fixture world's findings

Linting the repository's own reconciled world at the fixture's reference date
(`2026-08-12T09:00:00Z`) yields 45 findings, including each of the six
seeded imperfections from `data/README.md` as a distinct finding:

```text
#1 failed provenance observation   observation-result              FAIL    artifact:customer-agent-2.8.4-unsigned
#2 stale observation                observation-validator-staleness STALE  artifact:customer-agent-2.8.1 (provenance-prober@3.1.5)
#3 expiring exception               exception-expiry                EXCEPTION exception:emergency-deployment-path (2026-09-01)
#4 stale supplier assessment        supplier-assessment-freshness   STALE   supplier:aws (2026-06-01)
#5 missing supplier mapping         requirement-supplier-mapping    UNKNOWN requirement:nis2-article-21-supply-chain
#6 unresolved generic risk          unmodeled-reference             UNKNOWN ARR-RISK-044
```

The remaining findings are the same checks doing their work over the rest of
the world: the two ownerless production resources, the six unresolved typed
edges, every retained unmodeled reference, and the repository's two
declared-versus-observed conflicts.

## Out of this slice

- **Fixing or suppressing findings** — the linter reports; remediating the
  world is separate work.
- **Rendering findings into a compliance narrative** — the final milestone.
  The linter produces the structured findings a renderer consumes.
- **Writing findings back into the graph** — findings are returned as
  records; persisting them as graph state is a later milestone's concern.

