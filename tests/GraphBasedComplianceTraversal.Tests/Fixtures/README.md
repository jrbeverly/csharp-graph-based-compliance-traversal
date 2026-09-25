# Derived test fixtures

Hand-curated fixtures derived from the repository's real world — the
[`data/`](../../../data/README.md) and `fixtures/` trees at the repository
root — for negative-path tests. Unlike the real world, these miniature
repositories deliberately violate loading rules so tests can assert error
behavior against stable, checked-in inputs.

- `broken-record/` — a `data/` record with malformed YAML.
- `broken-fixture/` — a `fixtures/` document with malformed JSON.
- `declared-observed-mismatch/` — the real bucket record paired with an AWS
  encryption response that deliberately disagrees with it (AES256 observed
  against KMS declared), for the reconciliation milestone's negative path.

Each world is loadable through `RepositoryDataLoader.Load(<world path>)` and
fails at exactly the deliberately broken file; tests address them via
`TestWorld.DerivedWorld` in the test project.

These files are never written by tests — treat them like the real world:
read-only.
