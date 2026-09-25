# Declared-versus-Observed Facts and the Validation State Model

The single shared model for recording what the organization **declares** should
exist versus what an external system **observes** actually exists, and for the
validation states every later component reasons about. The code equivalent of
this document is `ValidationState`, `FactSubject`, and `ResolvedFact`
(`src/GraphBasedComplianceTraversal.Engine/State/`), which is the contract the
Assertions, Linter, and Reporting milestones compile against. The test suite
pins the state enumeration to this document's content.

## Validation states

`ValidationState` is the one enumeration used everywhere downstream. It is
**not** re-invented per component.

| State | Meaning |
|---|---|
| `UNKNOWN` | No observation exists for the property, so the check has not been resolved. The **default** value of the enumeration — by construction, a slot that was never resolved reads `UNKNOWN`, never `PASS`. |
| `PASS` | The observed value matches the declared value. |
| `FAIL` | The observed value violates an invariant declared for the subject. |
| `NOT_APPLICABLE` | The check does not apply to this subject. |
| `EXCEPTION` | The check is covered by an approved exception. |
| `EXPIRED` | The basis for the check (for example an exception or an acceptance) has expired. |
| `STALE` | The last observation is too old to count as current evidence. |
| `CONFLICTING` | Declared and observed values disagree, or two sources conflict. |

## Fact subjects

A `FactSubject` is a graph subject (a resource, artifact, service, ...) that
carries **two independent fact sets** over the same property vocabulary:

```text
subject: aws:s3:prod-release-artifacts

declared:                    observed:
  encryption: aws:kms          encryption: AES256
  region: us-east-1            region: us-east-1
```

Both sets address the same property names, so a property can hold a declared
value and a differing observed value simultaneously. `Declare` writes only the
declared set and `Observe` writes only the observed set — recording one never
overwrites the other, and `Resolve` reports both values even when they
conflict.

Values are JSON nodes (`System.Text.Json.Nodes.JsonNode`), the same value
representation the IO layer uses, so structured values such as the
`encryption` mapping above compare structurally. A JSON null value carries no
information and is treated as absent.

## Resolution rules

`FactSubject.Resolve(property)` compares the two sets. Absence of an
observation always yields `UNKNOWN` — a declared value is intention, not
evidence, and can never imply a pass.

| Declared value | Observed value | State |
|---|---|---|
| absent | absent | `UNKNOWN` |
| present | absent | `UNKNOWN` |
| absent | present | `UNKNOWN` (no declared expectation to validate against) |
| present | present, deep-equal | `PASS` |
| present | present, different | `CONFLICTING` |

`ResolveAll` resolves every property in the union of both sets, ordered by
property name.

## Out of this slice

- **Populating subjects** from `data/**` and `fixtures/**` — a later
  milestone. This slice defines the container, not the loader. The Ingestion
  milestone (`docs/ingestion.md`) loads `data/**` into the graph; feeding
  subjects' declared and observed sets is separate work.
- **Deciding specific invariants** — the Assertions milestone. The generic
  declared-versus-observed comparison above yields `PASS`, `CONFLICTING`, or
  `UNKNOWN`; deciding `FAIL`, `NOT_APPLICABLE`, `EXCEPTION`, `EXPIRED`, or
  `STALE` for a particular check is that milestone's work, built on this
  shared enumeration.

