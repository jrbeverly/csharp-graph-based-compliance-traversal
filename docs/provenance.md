# Per-Fact Provenance and Source Authority

Every fact in the graph — every node, every edge, every property — can answer
"where did this come from?", and when two sources disagree, a declared
precedence table says which source to consult. The code equivalent of this
document is `SourceKind`, `ClaimType`, `ProvenanceRecord`, `FactProvenance`,
`SourceAuthorityRegistry`, and `SourcePrecedence`
(`src/GraphBasedComplianceTraversal.Engine/Provenance/`), which is the
contract the Ingestion, Assertions, and Reporting milestones compile
against. The test suite pins this document's behavior and the authority
table's content.

## Provenance records

A `ProvenanceRecord` attaches to any fact: a **node**, an **edge**, or a
**property**. It names the kind of source the fact was recorded from, the
locator of the originating file or fixture (repository-relative), and — where
the source provides one — a timestamp and a version.

| Source kind | What it names in this repository's world |
|---|---|
| `Declaration` | An organizational record under `data/**` (a resource, adoption, policy, ...). |
| `Terraform` | A statement from Terraform state (`fixtures/terraform/`). |
| `AwsObserved` | An observation from a cloud API response (mocked AWS fixtures). |
| `Github` | Metadata from a code-host API response (mocked GitHub fixtures). |
| `Ci` | A CI workflow-run record (mocked CI fixtures). |
| `ProvenanceAttestation` | A provenance attestation (mocked Sigstore/SLSA statement). |
| `Unknown` | No known source. The default, by construction — see below. |

A fact may carry several records at once. The bucket `encryption` property,
for example, is stated three times in the fixture world — by the
organization's resource record, by the Terraform state fragment, and by the
mocked AWS observation — and its `FactProvenance` keeps all three records, so
each statement is traceable to its originating file:

```text
property: aws:s3:prod-release-artifacts.encryption
sources:
  - kind: declaration       locator: data/cloud/aws/resources/s3-prod-release-artifacts.yaml
  - kind: terraform         locator: fixtures/terraform/release-storage-resource.json
  - kind: aws-observed      locator: fixtures/aws/s3/get-bucket-encryption-response.json
```

Attaching provenance never overwrites or resolves the fact's value; it only
records where each statement came from.

## Facts carry no fabricated provenance

A fact with no known source is marked as such. `ProvenanceRecord.None` is the
explicit marker (kind `Unknown`), and `FactProvenance.None` is the empty
collection. A node, edge, or property never assigned provenance reads as
`Unknown` — never as `Declaration` and never as authoritative. `Unknown` is
the default value of the enumeration, so a provenance slot that was never
filled cannot be mistaken for a real source, and no authority table may rank
it.

## Source authority per claim type

When two sources disagree, precedence is declared rather than arbitrary.
`SourceAuthorityRegistry.Slice` ranks, for each claim type, which source
kinds are authoritative over it, most authoritative first, with the
rationale for every entry:

| Claim type | Precedence (most authoritative first) | Why |
|---|---|---|
| `Configuration` — resource configuration facts (encryption, region, access) | `AwsObserved` → `Terraform` → `Declaration` | The live API observation is ground truth for what is actually configured; Terraform state says what infrastructure-as-code has applied, which can lag or drift; the organization's record states intent. |
| `Derivation` — facts linking an artifact to its build, commit, or repository | `ProvenanceAttestation` → `Ci` → `Github` → `Declaration` | The signed attestation binds the artifact to the build that produced it; the CI record ties the build to its run and head commit; code-host metadata records the commit and repository; the organization's records state the intended build path. |
| `Organization` — policies, adoptions, approvals, ownership | `Declaration` → `Github` | The organization's own governance records are authoritative for its decisions; code-host metadata corroborates. |

A source kind without an entry for a claim type has **no authority** over it
(a CI record says nothing about a bucket's encryption), and comparing two
unranked sources yields `Incomparable` — precedence is undefined, and the
registry says so rather than picking one. Comparing a source with itself
yields `Equal`.

## Consulting the precedence

- `RankFor(claimType, kind)` — the kind's rank (1 = most authoritative), or
  `null` when it has no authority over the claim type.
- `RankedKinds(claimType)` — the precedence list itself, most authoritative
  first.
- `Compare(claimType, left, right)` — `LeftWins`, `RightWins`, `Equal`, or
  `Incomparable`. A ranked source always wins over an unranked one.
- `MostAuthoritative(claimType, records)` — the record whose source carries
  the highest authority for the claim type, or `null` when none does.

For a configuration fact stated by all three sources above, the source to
consult is the AWS observation — by declared precedence, not by chance:

```text
Compare(Configuration, declaration, aws-observed)  → RightWins
Compare(Configuration, terraform, aws-observed)    → RightWins
MostAuthoritative(Configuration, sources)          → the aws-observed record
```

The registry only ranks sources. Representing a conflict between two values
and reconciling it into a single one is the reconciliation milestone's work —
`MostAuthoritative` picks the source to consult, it never resolves values.

## Out of this slice

- **Populating provenance from `data/**`** — done by the Ingestion
  milestone's declared-record loader (`docs/ingestion.md`), and the
  `fixtures/**` side is populated by the external-source adapters (see
  `docs/adapters.md`). This slice defines the record, the attachment points,
  and the authority table.
- **Resolving conflicts into a single value** — the reconciliation milestone.
  The declared-versus-observed comparison in `docs/state-model.md` reports
  `CONFLICTING`; this slice says which source to consult about it.
- **Cryptographic verification** of attestations or signatures. The
  provenance record locates the attestation; verifying it is a connector's
  concern, not this slice's.


