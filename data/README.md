# Organizational Graph Fixture

## Overview

This directory contains a self-contained, deterministic fixture representing a fictional organization called **NexusDefend**. The fixture is designed as input data for a future graph-based compliance traversal system as described in [VISION.md](../VISION.md).

No AWS account, GitHub organization, external database, or live service is required. All external state is represented as local mock API responses in the `fixtures/` directory.

## The Fictional Organization

**NexusDefend** distributes a software agent (**Customer Agent**) to enterprise customers for endpoint security management. The agent receives signed software updates through a CloudFront-backed release distribution service.

### Primary End-to-End Path

```
Business Purpose: Secure enterprise endpoint management
    ↓ fulfills
Product: Customer Agent
    ↓ has-capability
Capability: Software Update Delivery
    ↓ implemented-by
Service: Release Distribution Service
    ↓ depends-on
CloudFront Distribution: customer-downloads
    ↓ origin
S3 Bucket: prod-release-artifacts
    ↓ contains
Artifact: customer-agent@2.8.4
    ↓ built-by
Build Pipeline: Customer Agent Release Pipeline
    ↓ source
Source Repository: customer-agent
```

### Security / Governance Path

```
Risk: Unauthorized modification of release artifacts
    ↓ results-in
Business Impact: Compromise of customer-distributed software
    ↓ informs
Control Adoption: ARR-3-11@4.2 (Secure Software Artifact Supply Chain)
    ↓ implemented-by
Implementation: Signed Release Pipeline
    ↓ validated-by
Assertion: Production artifact provenance validation
    ↓ produces
Observations: PASS, PASS, FAIL, PASS
```

### Compliance Path

```
Framework: NIS2 Directive
    ↓ requires
Requirement: Article 21 — Supply Chain Security
    ↓ satisfied-by
Control Adoption: ARR-3-11@4.2
    ↓ implemented-by
Implementation: Signed Release Pipeline
```

## Directory Layout

```
data/
├── README.md                          # This file
├── business/
│   ├── purposes/                      # Business purposes
│   ├── products/                      # Products
│   └── capabilities/                  # Product capabilities
├── technical/
│   ├── services/                      # Services
│   ├── repositories/                  # Source repositories
│   ├── build-pipelines/              # CI/CD build pipelines
│   └── artifacts/                     # Software artifacts
├── cloud/
│   └── aws/
│       └── resources/                 # AWS cloud resources
├── suppliers/                         # External suppliers
├── security/
│   ├── risks/                         # Risk definitions
│   ├── impacts/                       # Business impacts
│   ├── assumptions/                   # Explicit assumptions/context
│   ├── controls/
│   │   ├── definitions/              # External control specifications
│   │   ├── adoptions/                # Organization-specific control adoptions
│   │   └── implementations/          # Control implementation details
│   ├── assertions/                    # Security assertions
│   ├── observations/                  # Assertion observation history
│   └── exceptions/                    # Control exceptions / risk acceptances
└── compliance/
    ├── frameworks/                    # Regulatory frameworks
    └── requirements/                  # Framework requirements

fixtures/
├── aws/
│   ├── cloudfront/                    # Mock CloudFront API responses
│   └── s3/                            # Mock S3 API responses
├── github/                            # Mock GitHub API responses
├── ci/                                # Mock CI/CD workflow run responses
├── provenance/                        # Mock Sigstore provenance attestations
└── terraform/                         # Mock Terraform state fragments
```

## Design Principles

- **Stable identities**: Every object has a stable `id` field (e.g., `artifact:customer-agent-2.8.4`). Display names are not used as identities.
- **Composable records**: Small, independently meaningful files rather than a single monolithic document.
- **Explicit relationships**: Objects reference each other by stable identifiers, enabling traversal.
- **Provenance-ready**: External state fixtures represent the integration boundary — a future connector would parse these into normalized graph entities.
- **Deterministic**: All data is static and local; no network access required.

## External Control Specification

`ARR-3-11` is a fictional third-party control specification for "Secure Software Artifact Supply Chain" at version 4.2. It defines six requirements for build integrity, artifact signing, provenance, storage immutability, and distribution validation.

The organization **adopts** the standard (via `control-adoption:ca-artifact-supply-chain`) rather than copying its requirements into organization-specific policy. This demonstrates the separation between:

```
Control Definition (ARR-3-11@4.2)
    ↓ adopted-as
Control Adoption (ca-artifact-supply-chain)
    ↓ implemented-by
Implementation (signed-release-pipeline)
```

## Intentionally Imperfect Data

The fixture includes deliberate imperfections to give future validation logic meaningful findings to detect:

| # | Imperfection | Location | Description |
|---|-------------|----------|-------------|
| 1 | **Failed provenance observation** | `security/observations/provenance-observations.yaml` (`obs-2026-08-06-001`) | An unsigned artifact was submitted to production; the validation gate correctly blocked it. Related incident: INC-2026-041. |
| 2 | **Stale observation** | `security/observations/provenance-observations.yaml` (`obs-2026-07-15-001`) | The observation was produced by validator version 3.1.5 while current is 3.1.7. |
| 3 | **Expiring exception** | `security/exceptions/emergency-deployment-path.yaml` | Exception for emergency deployment bypass expires 2026-09-01 (~3 weeks from the fixture's reference date). |
| 4 | **Stale supplier assessment** | `suppliers/aws.yaml` | Last supplier risk assessment dated 2026-06-01, more than 60 days before the reference date. |
| 5 | **Missing supplier-to-requirement mapping** | `compliance/requirements/nis2-article-21-supply-chain.yaml` | NIS2 supply chain requirement maps to artifact controls but not to the AWS supplier relationship. |
| 6 | **Unresolved generic risk reference** | `security/risks/artifact-tampering.yaml` | References `ARR-RISK-044` from a shared risk library that has not yet been locally ingested. |

## Usage

These files are intended as deterministic inputs for a future graph engine implementation. A developer should be able to:

1. Inspect any file and understand the entity it represents.
2. Follow `id` references to trace relationships manually.
3. Trace the three acceptance-criteria paths (business→technical, risk→observation, requirement→implementation).
4. Identify the deliberate imperfections listed above.

No graph engine, traversal system, or report compiler is included — those are future implementation concerns.



