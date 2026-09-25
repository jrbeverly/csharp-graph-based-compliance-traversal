# Declared-versus-Observed Reconciliation and Conflict Surfacing

The reconciliation of the two worlds the graph is fed from: the facts the
organization's records **declare** (`data/**`, loaded by
`DeclaredRecordsLoader`) and the facts the external-source adapters
**observe** (`fixtures/**`, normalized through `IExternalSourceAdapter`).
The code equivalent of this document is `DeclaredObservedReconciler` and
`FactReconciliation`
(`src/GraphBasedComplianceTraversal.Engine/Reconciliation/`), with the
verdicts stored on the typed property graph (`Reconciliations`,
`RecordReconciliation`, `GetReconciliations`). The test suite pins this
document's behavior against the repository's own world and against the
curated `declared-observed-mismatch` derived world.

## The pipeline

`DeclaredObservedReconciler.ReconcileAll(graph, observedStates)` runs after
the declared facts are in the graph:

```text
data/**  →  DeclaredRecordsLoader  →  typed property graph
fixtures/**  →  adapters  →  NormalizedState  →  ReconcileAll  →  verdicts on the graph
```

An observed fact is matched to its declared subject by **stable id** — the
same identifier the repository records and the adapters emit, which the
records' `declared_by` / `observed_by` pointers anchor. For every subject
property both worlds state, the reconciler records a verdict; the observed
facts are then applied to the graph, so they are attached with their
provenance.

## Corroboration

When the observed value deep-equals the declared value, the verdict is
`PASS`: the observation **corroborates** the declaration. The record carries
the shared value from both sides, each with its own provenance, so the fact
is queryable as agreeing across independent sources:

```text
subject: aws:s3:prod-release-artifacts
property: encryption
state: PASS
declared:
  value: {algorithm: aws:kms, key_arn: arn:aws:kms:..., bucket_key_enabled: true}
  provenance: declaration  data/cloud/aws/resources/s3-prod-release-artifacts.yaml
observed:
  value: {algorithm: aws:kms, key_arn: arn:aws:kms:..., bucket_key_enabled: true}
  provenance: aws-observed  fixtures/aws/s3/get-bucket-encryption-response.json
              terraform     fixtures/terraform/release-storage-resource.json
```

Several observed statements of the same value merge into one observed side
carrying every statement's provenance — the bucket's encryption above agrees
across three independent sources.

## Conflicts

When the values disagree, the verdict is `CONFLICTING`: the record carries
**both values and both provenances**, plus the applicable source-authority
information — the claim type the property answers to, the precedence the
authority table declares for it, and the most authoritative source record.
Nothing is overwritten: the declared value entered the graph first and stays
the node's property value, so the disagreement is represented next to it,
never silently reconciled into a single value. Which side "wins" is a human
decision the model only represents — the precedence is recorded, not applied.

The injected mismatch world (`declared-observed-mismatch/`) pairs the real
bucket record with a mocked AWS response that observes AES256 where the
record declares KMS:

```text
subject: aws:s3:prod-release-artifacts
property: encryption
state: CONFLICTING
declared:  {algorithm: aws:kms, key_arn: ..., bucket_key_enabled: true}
           declaration  data/cloud/aws/resources/s3-prod-release-artifacts.yaml
observed:  {algorithm: AES256, bucket_key_enabled: false}
           aws-observed fixtures/aws/s3/get-bucket-encryption-response.json
claim type: Configuration
precedence: observed side wins (aws-observed ranks above declaration)
```

The real world carries genuine disagreements too: the repository record and
the code host state different descriptions and owners for
`repository:customer-agent`, and each surfaces as a `CONFLICTING` verdict
with both sides attributable.

## What gets a verdict

- **Both worlds state the property** → a `PASS` or `CONFLICTING` verdict.
- **Only the observed world states it** (the bucket's policy, the build run,
  the commit) → attached as an observed fact, no verdict — there is nothing
  declared to judge it against.
- **Only the declared world states it** → the declared fact stands as is.
- **A reference field the ontology maps to a typed edge** (`origins`,
  `contains`, `scope`, ...) → no value verdict: relationship facts are
  reconciled through their edges, whose merge retains every source record.
  The declared record references the target by node id while an adapter may
  state the same relationship in the external system's own vocabulary, so a
  value comparison would report a false conflict.

The claim type is derived from the subject: cloud resource properties are
`Configuration` claims, artifact and build properties are `Derivation`
claims, and everything else — repository metadata, ownership, governance
records — are `Organization` claims, which code-host metadata corroborates.

Reconciliation is idempotent: re-running it over an already reconciled graph
records no duplicate verdicts and overwrites no value.

## Out of this slice

- **Deciding which side wins** — the verdict records the precedence to
  consult; resolution is a human concern.
- **Assertion evaluation, linting, and reporting** — later milestones that
  consume these verdicts as findings.

