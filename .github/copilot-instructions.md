# GitHub Copilot Instructions

## Primary instruction source

Use the repository root [`AGENTS.md`](../AGENTS.md) as the **primary** source of truth for behavior,
architecture context, testing standards, and completion criteria.

If this file and `AGENTS.md` appear to conflict, prefer `AGENTS.md` unless this file explicitly states a
GitHub Copilot-only exception.

## Copilot-specific guidance

This file should only contain **GitHub Copilot-specific** instruction details.
Keep product, architecture, and general engineering standards centralized in `AGENTS.md`.

## Operating expectations for Copilot

- Apply the `AGENTS.md` testing bar strictly: TUnit tests, `*.UnitTests`/`*.IntegrationTests` project
  placement, and the rule that WSLC integration modules run serially.
- Treat work as incomplete until the relevant tests pass; unit tests need no WSLC host, integration tests do.
- Never weaken the WSLC session/store invariants (single shared session, serialised session mutations, the
  shared-store fallback, `Secret` redaction) to make a test pass.
- Treat the published consumer contract as an invariant too: `.NET 11` + a Windows target framework,
  the `buildTransitive` defaults, and the `PWC0001`/`PWC0002` guards documented in
  `docs/wiki/Consumer-Requirements.md`. `just verify-consumers` must pass before a consumer-facing change
  is complete.
- Consult the repository `.agents/` folder for additional skills/workflows that may improve execution
  quality. Skill content is delivered by NuGet packages and is read-only here.
- Keep edits minimal, focused, and aligned with existing SDK and repository conventions.
