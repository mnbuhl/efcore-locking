# Framework Support Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Publish a PR from latest main supporting .NET 10/11 and the agreed EF Core ranges with Microting MySQL.

**Architecture:** Preserve the four package boundaries and public API. Keep stable provider dependencies and explicitly test the supported runtime/provider combinations.

**Tech Stack:** .NET 10/11, EF Core, Npgsql, Microting, SQL Server, xUnit, GitHub Actions.

**Spec:** docs/superpowers/specs/2026-11-12-framework-support-design.md

## Global Constraints

- Target `net10.0;net11.0`; remove .NET 8/9.
- Allow EF Core, Npgsql and SQL Server `[10.0.0,12.0.0)`.
- Use stable Microting and respect its upstream constraints.
- Open a PR; do not merge or release.

## Review Focus

- Inspect nupkg metadata for framework and dependency range correctness.
- Run SQL generation against real provider services to catch changed internal APIs.
- Exercise SQL Server EF Core 11 separately from mixed-provider EF Core 10 projects.
- Ensure CI installs the SDK and runtimes needed by every target.
- Record any provider publication or local container limits without claiming unsupported verification.

## Task 1: Frameworks and dependencies

- [x] Update src/tests project targets, central baselines, SDK selection, Microting namespaces, samples and benchmarks.
- [x] Restore/build; run existing unit and provider SQL-generation tests on supported combinations.

## Task 2: CI and documentation

- [x] Update CI, manual compatibility workflow, and release SDK setup; test both runtimes and SQL Server EF Core 11.
- [x] Update support and migration documentation.

## Task 3: Verify and publish

- [x] Run formatting, builds, tests and package metadata checks; review final diff.
- [ ] Commit, push, open PR using repository template if present, and attach the PR.

## Execution evidence

- Release build succeeds with zero warnings and errors; formatting passes.
- EF Core 10: 1,116 tests pass across .NET 10 and 11, including PostgreSQL, MySQL and SQL Server integration tests.
- EF Core 11: 91 core tests and 176 SQL Server tests pass on .NET 11. Resolved test assets confirm EF Core/Relational/SQL Server 11.0.0.
- All four packed libraries contain only net10.0/net11.0 assemblies and the intended bounded dependency ranges.
- Independent review caught a missing direct SQL Server test dependency; fixed and verified against the actual EF Core 11 graph. No remaining findings.
- Local database runs use Rancher Desktop with its VM-side Docker socket override; no repository changes were needed for the local setup.
