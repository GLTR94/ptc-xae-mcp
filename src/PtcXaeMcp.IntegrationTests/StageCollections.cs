namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// Collection fixture that brings the live environment up before any Stage XAE
/// test runs (start the MCP host if it is down, open a TEMP copy of the neutral fixture
/// if a different/no project is loaded). All work is delegated to the run-once,
/// idempotent <see cref="LiveXae.EnsureLiveAsync"/>; in non-live mode it is a
/// no-op. Stage XAR funnels through the same call inside
/// <see cref="XarRuntimeFixture"/>, so either stage bootstraps in isolation.
/// </summary>
public sealed class LiveBootstrapFixture : IAsyncLifetime
{
    public Task InitializeAsync() => LiveXae.EnsureLiveAsync();

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>Stage XAE — TwinCAT Engineering operations (no runtime data needed).</summary>
[CollectionDefinition("Stage XAE")]
public sealed class StageXaeCollection : ICollectionFixture<LiveBootstrapFixture>;

/// <summary>
/// Stage XAR — operations that need the runtime in Run mode, including the activation itself. They all use
/// the explicit runtime target (<see cref="LiveXae.RuntimeNetIdEnvVar"/>, issue #195); without it the stage skips.
/// </summary>
[CollectionDefinition("Stage XAR")]
public sealed class StageXarCollection : ICollectionFixture<XarRuntimeFixture>;

/// <summary>Stage Scope — all scope tests (engineering + skip-gated recording).</summary>
[CollectionDefinition("Stage Scope")]
public sealed class StageScopeCollection : ICollectionFixture<LiveBootstrapFixture>;

/// <summary>Stage Motion — all NC/axis tests (config-only; no real movement).</summary>
[CollectionDefinition("Stage Motion")]
public sealed class StageMotionCollection : ICollectionFixture<LiveBootstrapFixture>;

/// <summary>Stage HMI — TwinCAT HMI (TE2000) against a TEMP copy of an HMI project; opt-in, runs last.</summary>
[CollectionDefinition("Stage HMI")]
public sealed class StageHmiCollection : ICollectionFixture<HmiFixture>;
