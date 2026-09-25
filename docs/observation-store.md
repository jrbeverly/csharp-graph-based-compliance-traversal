# The Observation Store and Historical Observation Ingestion

The store that holds the observations of every assertion — the repository's
recorded history and the observations freshly produced by evaluation — keyed
by assertion and subject, queryable as one chronological timeline per
assertion, and surfacing the staleness signal that marks observations
produced by an outdated validator version without discarding them. The code
equivalent of this document is `ObservationStore`, `StoredObservation`,
`HistoricalObservation`, `HistoricalObservationLoader`,
`ObservationRecordException`, and the internal `ValidatorVersion` parser
(`src/GraphBasedComplianceTraversal.Engine/Observations/`), the observations
milestone the Assertions slice pointed at. The test suite pins this
document's behavior against the repository's own `data/**` world.

## The store

`ObservationStore` holds observations keyed by assertion id and subject id
and answers them as a timeline: `Timeline(assertionId)` returns every
observation of the assertion across its subjects, `Timeline(assertionId,
subjectId)` the history of one subject — both ordered chronologically by
observation time, ties broken by ingestion order, so a query is
deterministic. Two entry points feed the store:

- `Record(observation)` — an `AssertionObservation` freshly produced by
  evaluating an assertion. The entry keeps the state, timestamp, provenance,
  and reason the evaluation produced; it carries no observation id and no
  validator version, so the staleness rule never flags it — an evaluation by
  the engine is current by construction.
- `Ingest(observation)` — a `HistoricalObservation` parsed from the
  repository's recorded history. The entry keeps its observation id, its
  recorded `PASS`/`FAIL` result, its timestamp, the validator version that
  produced it, its details, and its declaration provenance.

Nothing is discarded and nothing is rewritten. An ingested observation keeps
the state it was recorded with — a `FAIL` stays a `FAIL` — and a repeated
observation id is rejected, because recorded history names its entries and
two entries must not share a name.

## Ingestion of the recorded history

`HistoricalObservationLoader.ApplyAll(store, records)` walks the loaded
repository documents (`RepositoryDataLoader.Load(root).Records`) and, for
every document whose root carries an `observations` list, ingests each entry:
its `id`, `assertion`, `subject.artifact` (the subject key), `result`
(`pass`/`fail`), `observed_at`, `validator_version`, and `details`, plus the
optional `subject.digest` and `related_incident`, each with `Declaration`
provenance naming the document. An entry whose shape or field values are
malformed — an unparseable timestamp, a result other than `pass`/`fail`, a
validator version that does not read as `name@major.minor.patch` — fails
loudly with an `ObservationRecordException` naming the document's path:
recorded history is never silently dropped or repaired.

The history document
(`data/security/observations/provenance-observations.yaml`) contributes five
observations of `assertion:prod-artifact-provenance`: four passes across the
2.8.1–2.8.4 releases and the recorded failure of the blocked unsigned upload
(`observation:obs-2026-08-06-001`, with its `related_incident` reference).

## The timeline entry

Every timeline entry is a `StoredObservation`:

| Member | Meaning |
|---|---|
| `RecordedState` | The `ValidationState` the observation was recorded with — `PASS`/`FAIL` for ingested history, `PASS`/`FAIL`/`UNKNOWN` for evaluation. |
| `State` | The state the store surfaces: `STALE` when the observation is stale, else the recorded state. |
| `IsStale` | True when the observation was produced by a validator version older than the assertion's current validator version. |
| `ObservedAt` | The observation time — `observed_at` for history, the evaluation timestamp for evaluated observations. |
| `Provenance` | Where the observation came from: the history document's declaration record, or the provenance of the facts the evaluation consulted. |
| `Reason` | The history entry's details, or the evaluation's reason naming the deciding facts. |
| `ObservationId` | The history entry's id (`observation:obs-...`); `null` for freshly evaluated observations. |
| `ValidatorVersion` | The validator version that produced the history entry (`provenance-prober@3.1.7`); `null` for evaluated observations. |
| `SubjectDigest` | The history entry's `subject.digest`, when the entry records one. |
| `RelatedIncident` | The history entry's `related_incident` reference, when the entry records one. |

## The staleness signal

The store derives each assertion's current validator version from its own
history — the newest validator version recorded across the assertion's
observations (`CurrentValidatorVersion(assertionId)`, `null` when none
exists). An observation produced by an older version of the same validator
is surfaced `STALE`: its `RecordedState` stays intact and it remains
queryable, so outdated evidence is visibly outdated instead of being
smoothed into current evidence. The comparison is numeric — `3.1.10`
outranks `3.1.7` — and observations produced by a different validator are
never flagged by this rule, since two validators cannot be ordered.

For `assertion:prod-artifact-provenance` the history records three validator
versions — `provenance-prober@3.1.5`, `@3.1.6`, and `@3.1.7` — so the
current validator is `provenance-prober@3.1.7`, and the 3.1.5 and 3.1.6
observations are surfaced stale:

```text
artifact:customer-agent-2.8.1            PASS  2026-07-15T08:00:00Z  STALE  provenance-prober@3.1.5
artifact:customer-agent-2.8.2            PASS  2026-08-05T11:30:05Z  STALE  provenance-prober@3.1.6
artifact:customer-agent-2.8.4-unsigned   FAIL  2026-08-06T23:42:11Z  FAIL   provenance-prober@3.1.7
artifact:customer-agent-2.8.3            PASS  2026-08-07T09:15:44Z  PASS   provenance-prober@3.1.7
artifact:customer-agent-2.8.4            PASS  2026-08-08T14:01:22Z  PASS   provenance-prober@3.1.7
```

The failure is preserved with its state and its incident reference, and the
stale entries are preserved with their recorded passes — the store surfaces
staleness, it never discards the history behind it.

## One timeline for history and evaluation

Recorded history and freshly evaluated observations coexist in the same
store and the same timeline. Evaluating the slice against the loaded
declared graph and recording the produced observations appends them to the
history: the provenance assertion's timeline then holds its five historical
observations followed by the freshly evaluated one, ordered by observation
time.

## Out of this slice

- **Deciding compliance outcomes from the history** — the reporting
  milestone. The store exposes the history; it never aggregates it into a
  verdict.
- **Age-based staleness** — an observation too old to count as *current
  evidence* is decided by the linter's freshness invariants. This slice's
  staleness signal is the validator-version comparison.
- **Structural linting and graph-health scoring** — the linter issue.
- **Writing observations back into the graph** — the store holds
  `StoredObservation` records; persisting them as `Observation` nodes is a
  later milestone's concern.

