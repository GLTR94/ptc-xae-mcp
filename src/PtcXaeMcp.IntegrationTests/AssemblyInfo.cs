using PtcXaeMcp.IntegrationTests;
using Xunit;

// Narrate the live run: fire a start/end ptc_test_marker around every test so
// the host's MCP Activity tab shows which test is currently running (i/N).
// Best-effort and no-ops when the host is unreachable — never affects a test.
[assembly: LiveTestTrace]

// The integration suite drives a SINGLE, single-threaded live TwinCAT XAE
// through the MCP server. The server rejects (does not queue) concurrent
// calls with "[Error] An operation is already in progress.", so running test
// classes in parallel makes the pass/fail outcome depend on scheduling luck
// rather than on whether the tooling works. Serialize the whole assembly so
// The live suite is deterministic by construction. See issue #17.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
