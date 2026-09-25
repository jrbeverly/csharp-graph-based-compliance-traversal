# External-Source Adapters and Normalized State

The mocked external world is normalized into the graph through a contract that
mirrors the eventual live boundary. The code equivalent of this document is
`IExternalSourceAdapter`, `ExternalSourceAdapter`, `NormalizedState`,
`FixtureAdapterRegistry`, and the concrete adapters
(`src/GraphBasedComplianceTraversal.Engine/Adapters/`), which the test suite
pins to the repository's `fixtures/**` documents.

## The boundary

A live connector will eventually look like this:

```text
API
 ↓
Adapter
 ↓
Normalized State
 ↓
Graph
```

The mock world substitutes the first step with the repository's fixture files:

```text
Mocked API Response (fixtures/**)
 ↓
Adapter
 ↓
Normalized State
 ↓
Graph
```

The graph and every downstream component are indifferent to which of the two
steps produced a fact: both feed the same adapters, and the adapters emit the
same normalized facts. An adapter never performs network access and never
requires credentials — it reads only the parsed response it is handed.

## The adapter contract

`IExternalSourceAdapter` takes **one parsed response** and produces **one
normalized state**:

- Input: a `RepositoryDocument` — the parsed JSON content plus its locator
  (the repository-relative fixture path in this world). The locator is what
  every emitted fact's provenance points back at. A live connector constructs
  the same document from a parsed HTTP response body.
- `CanNormalize(document)` — whether the response has the shape the adapter
  normalizes. Shape checks are mutually exclusive, so a document matches at
  most one adapter.
- `Normalize(document, availableDocuments)` — the normalized facts. The second
  parameter is the rest of the loaded world, letting an adapter resolve
  references its response makes to other source documents (the attestation
  resolves the workflow run its invocation id names); adapters that make no
  cross-document references ignore it. A malformed or incomplete response
  throws an `AdapterException` naming the response's locator — never a silent
  partial fact set.

The concrete adapters compile against the ontology slice; `NormalizedState.ApplyTo`
validates every node and edge against the graph's registry on the way in.

## Normalized state

`NormalizedState` is the adapter output: `NormalizedNode` (stable id, declared
node type, properties), `NormalizedEdge` (name with explicit endpoint types),
and `NormalizedProperty` (name, JSON value) — every one carrying a
`FactProvenance` with the source kind and locator of the fact.

`ApplyTo(graph)` projects the state into the typed property graph and
**merges**:

- Facts about the same subject from several adapters (the bucket's Terraform
  state and its two AWS observations) accumulate on one node; the node's
  provenance and each property's provenance keep every source record.
- A property stated more than once keeps the first value, while every
  statement's provenance is retained — representing a disagreement between
  values is the reconciliation milestone's work (`docs/reconciliation.md`).
- The same edge stated by two adapters (the CI record and the attestation both
  tie the build run to its commit) stays one edge carrying each distinct
  source record.

## The fixture reader

`FixtureAdapterRegistry.MockWorld` is the stand-in for a live connector: it
enumerates the repository's `fixtures/**` documents and dispatches each to its
adapter (`NormalizeAll` / `ApplyAll`), rejecting a document no adapter
matches. It is the one place the mock world's subject bindings live — the
identities the mocked responses do not state themselves:

| Adapter | Subject binding | Why the response does not carry it |
|---|---|---|
| `aws.s3.get-bucket-encryption` | `aws:s3:prod-release-artifacts` | The real API names the bucket in the request path, not the body. |
| `aws.cloudfront.get-distribution-config` | `aws:cloudfront:customer-downloads` | Same: the distribution id is a request-path parameter. |
| `sigstore.provenance-attestation` | `artifact:customer-agent-2.8.4` | The attestation identifies its subject by digest; the mock world's attestation attests `customer-agent` 2.8.4 (the fixture's `$description`). |

Every other adapter derives its subject from the response itself: the policy
names its bucket in every statement's resource ARN, the Terraform fragment
names it in the bucket resource's attributes, the repository response carries
its `name`, the commit response its `sha`, and the workflow-run response its
`id`, `head_sha`, and `repository.full_name`.

A live connector replaces only this registry: it calls the API, wraps the
parsed response in a document, and feeds it to the same adapter bound to the
subject it queried.

## What each adapter normalizes

| Fixture | Adapter | Kind | Facts emitted |
|---|---|---|---|
| `aws/s3/get-bucket-encryption-response.json` | `aws.s3.get-bucket-encryption` | `AwsObserved` | Bucket node property `encryption` (`algorithm`, `key_arn`, `bucket_key_enabled`). |
| `aws/s3/get-bucket-policy-response.json` | `aws.s3.get-bucket-policy` | `AwsObserved` | Bucket node (id derived from the statements' resource ARNs) property `policy` (`version`, normalized `statements`). |
| `aws/cloudfront/get-distribution-config-response.json` | `aws.cloudfront.get-distribution-config` | `AwsObserved` | Distribution node properties (`enabled`, `aliases`, `origins`, `viewer_protocol_policy`, `minimum_protocol_version`, `http_version`, `ipv6_enabled`, `price_class`) and the `origin` edge to the bucket named by the default origin's domain. |
| `terraform/release-storage-resource.json` | `terraform.state-fragment` | `Terraform` | Bucket node (id derived from the bucket resource's attributes) with the applied state: `arn`, `force_destroy`, `tags`, `versioning`, `encryption`, `public_access_block`. |
| `github/repository-response.json` | `github.repository` | `Github` | Repository node `repository:customer-agent` with the code-host metadata (`name`, `full_name`, `url`, `description`, `default_branch`, `language`, `private`, `archived`, `visibility`, `topics`, `owner`). |
| `github/commit-response.json` | `github.commit` | `Github` | Commit node `commit:{sha}` with `sha`, `message`, `author`, `committer`, `committed_at`, `parents`. The response does not name its repository, so the commit-to-repository edge comes from the sources that name both. |
| `ci/workflow-run-response.json` | `ci.workflow-run` | `Ci` | Build-run node `build-run:{id}` with the run record (`name`, `head_branch`, `head_sha`, `run_number`, `event`, `status`, `conclusion`, timestamps), plus the `built-from` edge to its head commit and the `committed-to` edge to its repository. |
| `provenance/sigstore-attestation.json` | `sigstore.provenance-attestation` | `ProvenanceAttestation` | Artifact node property `digest` (validated against the SLSA statement shape), the `produced-by` edge to the build run the invocation id names, and — reading the run's head commit and repository from the workflow-run document — the `built-from` and `committed-to` edges completing the chain. |

Facts read from a companion document carry that document's provenance: the
`built-from` and `committed-to` edges established from the workflow-run record
carry a `Ci` record even when emitted by the attestation adapter, while the
`produced-by` edge carries both the attestation record (with version
`slsa-provenance/v1`) and the CI record.

## The derived linkage

The attestation and the workflow-run record together establish the
derivation chain the acceptance walkthrough traverses:

```text
artifact:customer-agent-2.8.4
        │ produced-by   (attestation + CI record)
        ▼
build-run:84125
        │ built-from    (CI record)
        ▼
commit:7a94f3e…
        │ committed-to  (CI record)
        ▼
repository:customer-agent
```

The `built-from` and `committed-to` edges are also emitted by the CI adapter;
`ApplyTo` merges the two statements into one edge per relationship, retaining
each distinct provenance record. For a derivation fact the authority table
declares the attestation the source to consult, ahead of the CI record — see
`docs/provenance.md`.

## Swapping fixtures and swapping connectors

A fixture file may be swapped for another well-formed response of the same
shape without touching any downstream code: the same adapter instance
normalizes it, and only the observed facts change. Because adapters read only
parsed content, the locator need not even name an existing file — a live
connector passes a synthetic locator (an API identifier, a URL, a snapshot
id) in its place.

## Out of this slice

- **Reconciling observed facts against declared subjects** — the
  reconciliation milestone (`docs/reconciliation.md`). `ApplyTo` keeps the
  first value and retains every source record; `DeclaredObservedReconciler`
  records `CONFLICTING` facts and their authority precedence next to the
  retained values.
- **Any real AWS/GitHub/Sigstore SDK or authentication**, and cryptographic
  verification of the attestation — the attestation adapter validates the
  statement's shape and cross-references its run and repository, it does not
  verify signatures.
