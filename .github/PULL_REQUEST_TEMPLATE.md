## Summary

<!-- What changes and why. Link the issue: Closes #… -->

## Verification

- [ ] CI `build-and-test` is green (compile without TwinCAT + unit tests).
- [ ] The change touches COM / Automation-Interface paths → full live suite run per
      [CONTRIBUTING.md](../CONTRIBUTING.md), exact counts here: `live … passed / … failed / … skipped`
      (or: "no COM paths touched").
- [ ] `dotnet build -c Release -p:TwinCat=false` still compiles (new COM code sits inside `#if TWINCAT`).
- [ ] Tool descriptions changed → `docs/mcp-capabilities.md` regenerated with `python tools/gen_tool_reference.py`.
- [ ] No customer names, project numbers, machine paths or credentials in code, tests, fixtures or docs.
- [ ] Every commit carries a `Signed-off-by:` line (DCO, see CONTRIBUTING.md).
