# ADR 0001 — Three-way test split (Unit / Mcp / Integration)

Status: accepted
Date: 2026-05-12

## Context

The ptc-xae-mcp project bridges three layers:

1. **Claude Code (or any MCP client)** speaks JSON-RPC over HTTP to our server.
2. **The C# WinForms host** parses requests, validates schemas, manages state, dispatches to TwinCAT.
3. **TwinCAT XAE** is driven via COM interop (`dynamic` calls into `TcXaeShell.DTE`, `LookupTreeItem("TIID")`, etc.).

Each layer has different testing constraints:

- The **Claude→C# boundary** is testable from a separate process by speaking MCP-over-HTTP. No XAE required, but the exe must be running and listening.
- The **C#→XAE boundary** is the hardest part of the system. Running XAE is heavy (Visual-Studio-based shell), state is non-trivial to reset between tests, and the COM surface is `dynamic`-typed so refactors easily silently break.
- **Pure C# logic** (JSON schema validation, name normalisation, command building) needs neither the exe nor XAE and should be fast and CI-friendly.

Bundling all three concerns into a single test project would force every test run to either depend on a running exe + XAE (slow, flaky in CI) or skip 90% of tests with traits.

## Decision

Split into three xUnit projects:

| Project | Layer covered | External dependencies |
|---|---|---|
| `PtcXaeMcp.Tests` | Pure C# logic | none |
| `PtcXaeMcp.McpTests` | Claude→C# (MCP protocol) | exe running on `localhost:5010` |
| `PtcXaeMcp.IntegrationTests` | C#→XAE (end-to-end) | exe + running TwinCAT XAE with fixture solution |

`McpTests` skip gracefully (custom `SkipException`) when the server is unreachable. `IntegrationTests` will use the same skip pattern when XAE isn't running.

CI runs only `PtcXaeMcp.Tests`. The other two are manual / scheduled local runs.

## Consequences

**Good**
- A failing build in any layer points to the right project.
- Adding a new MCP tool requires touching one or two test projects, not all of them.
- CI stays fast and never depends on a Windows GUI process or a TwinCAT installation.

**Bad**
- More projects to keep in sync (NuGet versions, target frameworks).
- Cross-project shared fixtures (e.g., schema JSONs) need careful placement.
- Developers must remember which project to add a new test to.

## Alternatives considered

- **Single project + xUnit Traits.** Simple but doesn't enforce isolation; an Mcp-traited test could accidentally call into the COM layer and nobody would notice until CI ran it.
- **Two projects (Unit + Integration).** Loses the distinction between "needs the running exe" and "needs the running exe AND a running XAE", which matters because the former is achievable in CI while the latter never will be.

## Where to add a new test

| You are testing... | Add it to |
|---|---|
| A pure function (schema parser, name normaliser, command builder) | `PtcXaeMcp.Tests` |
| An MCP tool's protocol behaviour (request shape, error mapping) — XAE not involved | `PtcXaeMcp.McpTests` |
| A behaviour that requires real XAE state | `PtcXaeMcp.IntegrationTests` |
