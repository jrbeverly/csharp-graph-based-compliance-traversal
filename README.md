# Graph-Based Compliance Traversal

Models declared and observed organizational facts as a typed graph, then traverses evidence paths and explains compliance findings.

```csharp
new PathGrammar(registry, "supply-chain-assurance")
    .Step("satisfied-by", Node(registry, "RegulatoryRequirement"), Node(registry, "ControlAdoption"))
    .Step("implemented-by", Node(registry, "ControlAdoption"), Node(registry, "ControlImplementation"))
    .Step("validated-by", Node(registry, "ControlImplementation"), Node(registry, "Assertion"))
    .Step("produces", Node(registry, "Assertion"), Node(registry, "Observation"));
```

Compiled narrative (from the `supply-chain-compliance.txt` golden):

```
"Signed Release Pipeline" (implementation:signed-release-pipeline) is validated by "Production artifact provenance validation" (assertion:prod-artifact-provenance).
"Production artifact provenance validation" (assertion:prod-artifact-provenance) produces observation:obs-2026-08-06-001, recorded fail at 2026-08-06T23:42:11Z by validator provenance-prober@3.1.7.

Supporting facts:
  [edge] validated-by: implementation:signed-release-pipeline → assertion:prod-artifact-provenance
    source: declaration data/security/assertions/prod-artifact-provenance.yaml
```

```sh
make test
make demo
```

## Notes

- The repository uses a deterministic fictional organization, NexusDefend, and local mock API responses; no live service is required.
- Facts retain provenance, authority, declared-versus-observed state, and validation history.
- Typed path grammars distinguish valid evidence chains from arbitrary graph reachability.
- Golden tests cover loading, reconciliation, assertions, linting, traversal, projections, and narrative output.
