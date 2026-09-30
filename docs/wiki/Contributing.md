# Contributing

Local workflow for the repository: prerequisites, the change loop, and what to check before raising a pull
request.

## Prerequisites

- Windows with **WSL ≥ 2.9.3** (WSL Containers) for integration tests; see [Getting Started](Getting-Started.md).
- .NET SDK 11 (pinned in `global.json` as `11.0.100-rc.1.26425.128`).
- [just](https://github.com/casey/just) for the repository recipes, and `dotnet tool restore` for the pinned
  local tools (CSharpier, dotnet-inspect).
- [Bun](https://bun.sh) only for `just version` and the commit hook tooling; the commit hook runs
  `npx commitlint` via lefthook (`.config/lefthook.yml`).

## The change loop

1. Inspect the working tree and locate the implementation, tests, documentation and existing patterns for
   the change.
2. Confirm behaviour from code and tests rather than memory or documentation alone.
3. Make the smallest coherent change; keep public behaviour unless the task explicitly changes it.
4. Add or update tests for behaviour changes, and update documentation when public behaviour changes.
5. Run the narrowest meaningful validation first, then broader validation in proportion to risk.

```powershell
just build                      # fast compile check
just test '/*/*/*/*[Category=Unit]'   # unit tests (no WSLC host needed)
just lint-fix                   # CSharpier format (just lint-check to verify only)
just pack                       # build + pack into ./artifacts
just pipeline-pack-validate     # full local gate: restore, build, lint, test, pack, validate
```

`just scrub` resets the repository (`bin`/`obj`, clean, forced restore, build-server shutdown) when a stale
restore or compiler state is suspected.

## Commits

Commit messages follow Conventional Commits (`commitlint.config.mts`): a lower-case type from
`build`, `chore`, `ci`, `docs`, `feat`, `fix`, `perf`, `refactor`, `revert`, `style`, `test`, an optional
scope, and a subject under 100 characters with no trailing full stop. The `commit-msg` lefthook runs
`npx commitlint --edit` and rejects anything else.

## Documentation expectations

A change is not complete until the affected documentation matches:

- **`src/src/<Project>/Sdk/README.md`** — the package's own README, shipped inside the `.nupkg`. Update it
  whenever a module's public API, defaults, readiness or endpoints change.
- **`docs/wiki`** — the user-facing wiki aggregated by the purview-dev website. Update the topic page
  ([Architecture](Architecture.md), [Lifecycle](Lifecycle.md), [Networking](Networking.md),
  [Wait Strategies](Wait-Strategies.md), [Modules](Modules.md), [Testing](Testing.md),
  [Packaging](Packaging.md), [Release Flow](Release-Flow.md)) and `_Sidebar.md` when adding a page.
- **`purview-build.json`** — the exhaustive `PackValidation.RequiredContent` manifest has to keep matching
  what the packages actually contain; see [Packaging](Packaging.md).
- **`README.md`** — the repository front page, when the shape of the project changes.

## Before you raise a PR

- Confirm the requested behaviour and scope are satisfied and that only intended files changed.
- Review public API, package-content and dependency-direction implications.
- Run `just lint-check`, the relevant tests, and `just pipeline-pack-validate` when packaging, dependencies or
  package assets changed.
- State exactly what validation ran; if something could not run, say why and what risk remains.

## Related

- [Contributing Modules](Contributing-Modules.md) — the module contract for new service packages.
- [Packaging](Packaging.md) — package contents and validation rules.
- [Release Flow](Release-Flow.md) — versioning and what CI does on a PR and on release.
