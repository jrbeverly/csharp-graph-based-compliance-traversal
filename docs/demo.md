# End-to-End Demonstration and Acceptance Walkthrough

The capstone run: one command loads the repository's declared facts and
mocked external reality, builds the typed graph, traverses the three
canonical cross-domain paths, evaluates the assertion slice, lints the
reconciled structure, and compiles the explainable supply-chain narrative —
with the fixtures' seeded imperfections left visible, never smoothed into a
perfect story. The code equivalent of this document is `DemoPipeline`
(`src/GraphBasedComplianceTraversal.Cli/DemoPipeline.cs`), and its output is
pinned byte for byte under
`tests/GraphBasedComplianceTraversal.Tests/Golden/demo.txt`.

## Run it

Inside the development container, from anywhere in the repository:

```sh
make demo
```

which is `make run ARGS="demo"` — the CLI command is:

```sh
dotnet run --project src/GraphBasedComplianceTraversal.Cli -- demo
```

The run is fully offline: no network access, no credentials, no external
service. It exits `0` on success and `1` with a `demo failed:` message when
the repository cannot be loaded.

## Read it

The output is one document in pipeline order; each numbered section is one
stage of the run.

1. **Facts loaded** — `RepositoryDataLoader.Load` over `data/**` (declared
   records) and `fixtures/**` (mocked external responses), both ordered by
   identifier and path.
2. **Mocked observations normalized** — `FixtureAdapterRegistry.MockWorld`
   normalizes every fixture document through its adapter into nodes, edges,
   and properties tagged with their source provenance.
3. **Typed connected graph** — the node and edge counts of the merged
   graph, plus what was retained rather than dropped: unresolved references
   (edges to records that do not exist), unmodeled references (fields the
   ontology declares unmodeled), and the declared-versus-observed
   reconciliations, with the count of `CONFLICTING` ones stated.
4. **Canonical path 1 — business ↔ infrastructure** — the resource-to-purpose
   chain from `aws:s3:prod-release-artifacts` to
   `business-purpose:secure-endpoint-management`, and the reverse walk that
   reports the two product capabilities without records as unresolved
   references rather than silently stopping.
5. **Canonical path 2 — infrastructure → source and provenance** — the
   artifact's declared build chain, the attested derivation the Sigstore
   provenance attestation binds it to, and its distribution.
6. **Canonical path 3 — risk → control and observations** — from
   `risk:artifact-tampering` through impact, adoption, implementation, and
   assertion to every observation produced.
7. **Assertion evaluation** — `AssertionRegistry.Slice` evaluated at the
   fixed reference timestamp, one line per subject with the state the
   subject's facts resolved to: the attested artifact and the adopted
   control pass, and the ownerless production resources resolve `UNKNOWN`,
   never an implied pass.
8. **Observation store** — the current state per assertion: the recorded
   history's pass/pass/fail/stale/stale timeline, where a recorded failure
   stays a failure and an outdated validator's entry stays visibly stale.
9. **Organizational linter findings** — the 45 findings over the reconciled
   world, each naming subject, invariant, state, and reason. The six seeded
   imperfections (see below) each appear as a distinct finding.
10. **Compiled supply-chain narrative** — the NIS2 requirement traced
    through the organization's actual control machinery by the supply-chain
    assurance grammar, every sentence templated from a path step with its
    supporting facts and provenance attached, the recorded `fail` and the
    outdated-validator `stale` surfaces kept in the prose.

The closing line summarizes the run: three canonical paths (11 explained
traversals), 4 assertion evaluations, 45 linter findings, 1 compiled
narrative.

### The six seeded imperfections

`data/README.md` documents six deliberate imperfections. Each stays visible
in the output rather than being read as success:

| # | Imperfection | Where it appears |
|---|---|---|
| 1 | Failed provenance observation | Section 8: `obs-2026-08-06-001 → fail`; section 9: the `FAIL` `observation-result` finding naming `INC-2026-041`; section 10: the narrative's "recorded fail" sentence |
| 2 | Stale observation (validator 3.1.5/3.1.6 behind 3.1.7) | Section 8: two `→ stale (recorded pass ...)` lines; section 9: two `STALE` `observation-validator-staleness` findings; section 10: "The store currently surfaces stale (recorded pass)." |
| 3 | Expiring exception | Section 9: the `EXCEPTION` `exception-expiry` finding — expires 2026-09-01, within 30 days of the reference date |
| 4 | Stale supplier assessment | Section 9: the `STALE` `supplier-assessment-freshness` finding — 2026-06-01 is 72 days before the reference date |
| 5 | Missing supplier-to-requirement mapping | Section 9: the `UNKNOWN` `requirement-supplier-mapping` finding — the chain reaches `supplier:aws` but the requirement maps to nothing |
| 6 | Unresolved generic risk reference | Section 9: the `UNKNOWN` `unmodeled-reference` finding for `ARR-RISK-044` |

## Reproducibility

- **Deterministic by construction.** Every stage orders its output by the
  repository's ordering rules (identifier, path, insertion, timeline), and
  the reference timestamp is a fixed constant — the fixture world's reference
  date `2026-08-12T09:00:00Z` — never the clock. The demonstration makes no
  network calls and reads no environment beyond the repository files.
- **Identical re-runs.** Running `make demo` against the unchanged
  repository produces byte-identical output, today or next month.
- **Golden artifact.** The full output is pinned as
  `tests/GraphBasedComplianceTraversal.Tests/Golden/demo.txt` and asserted
  byte for byte by `DemoPipelineTests`. A golden that changes without an
  intended output change means the fixture world itself changed — treat that
  as a data change. After an intended output change, regenerate with
  `make test-update-goldens` and review the diff.


