# Third-Party Notices

Pneutronic XAE MCP (`ptc-xae-mcp`) uses the following third-party components. Versions are the
ones referenced in the `.csproj` files at the time of writing; the license
column reflects each package's declared license at that version.

## NuGet packages (redistributed with the built/published product)

| Package | Version | License | Used by |
|---|---|---|---|
| ModelContextProtocol | 1.4.1 | Apache-2.0 | PtcXaeMcp.WinForms |
| ModelContextProtocol.AspNetCore | 1.4.1 | Apache-2.0 | PtcXaeMcp.WinForms |
| ModelContextProtocol.Core | 1.4.1 | Apache-2.0 | (transitive) |
| Beckhoff.TwinCAT.Ads | 7.0.389 | MIT | PtcXaeMcp.WinForms — managed ADS client, P/Invoke only at runtime |
| Microsoft.Data.Sqlite.Core | 9.0.21 | MIT | PtcXaeMcp.WinForms |
| SQLitePCLRaw.bundle_winsqlite3 | 2.1.11 | Apache-2.0 | PtcXaeMcp.WinForms |

## NuGet packages (build-time / test-only, not redistributed)

| Package | Version | License | Used by |
|---|---|---|---|
| xunit | 2.9.3 | Apache-2.0 | test projects |
| xunit.runner.visualstudio | 3.1.5 | Apache-2.0 | test projects |
| Xunit.SkippableFact | 1.5.85 | MS-PL | test projects |
| FluentAssertions | 7.2.2 | Apache-2.0 | test projects |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT | test projects |
| coverlet.collector | 6.0.4 | MIT | test projects |
| Appium.WebDriver | 5.2.0 | Apache-2.0 | PtcXaeMcp.AppiumTests only |

None of the build-time/test-only packages ship inside the published
`ptc-xae-mcp.exe` or the Inno Setup installer.

### envdte / envdte80 / stdole / Microsoft.VisualStudio.Interop — Microsoft interop assemblies (NOT redistributed)

`EnvDTE`, `EnvDTE80`, `stdole` and `Microsoft.VisualStudio.Interop` are
Microsoft-published interop assemblies for the Visual Studio / TwinCAT XAE
Shell Automation Interface. They are licensed under the **Microsoft Software
License Terms — Visual Studio Add-ons and Extensions** (use "solely with"
Visual Studio, no stand-alone redistribution), not an OSI license, so this
project does **not** ship them. Every XAE machine has them in a TcXaeShell
`Common7\IDE\PublicAssemblies` directory:

- 32-bit XAE (`Program Files (x86)\Beckhoff\TcXaeShell`): `envdte` / `envdte80`
  8.0.0.0 and `stdole` 7.0 (the `EnvDTE` 8.0.0.0 that `TcHmiAutomation.dll`
  references);
- 64-bit shell (`Program Files\Beckhoff\TcXaeShell`): `envdte` / `envdte80` /
  `stdole` 17.0 as facades over `Microsoft.VisualStudio.Interop` 17.0.

The build references them from there at compile time only (the 32-bit XAE's
8.0 copies when present, otherwise the 64-bit shell's), and
`BeckhoffAssemblyResolver` loads them at runtime, 64-bit shell first.

On a machine without TcXaeShell (hosted CI) the build falls back to the
`envdte` / `envdte80` 17.x NuGet packages so that the unit suite still builds
and runs; that output is never released, and `setup\build-installer.ps1`
refuses a publish folder that contains any `envdte*`, `stdole*` or
`Microsoft.VisualStudio.*` file.

## Beckhoff components (referenced from the TwinCAT installation, NOT redistributed)

The following components are proprietary Beckhoff Automation software.
They are **not** included in this repository, in the NuGet package cache
under source control, or in the built/published output. The build
references them only via `HintPath` into a local TwinCAT 3.1 installation
(`C:\Program Files (x86)\Beckhoff\TwinCAT\...`), and only when that
installation is present (see the `$(TwinCat)` MSBuild switch in
`PtcXaeMcp.WinForms.csproj`); a machine without TwinCAT XAE builds and
runs the non-COM majority of the code without them.

| Component | Source | Redistributed? |
|---|---|---|
| `TwinCAT.Measurement.AutomationInterface.dll` | `TwinCAT\Functions\TE130x-Scope-View\` (TE130x Scope View) | No — `EmbedInteropTypes=true`, interface-only, no runtime file dependency |
| `Beckhoff.TwinCAT.TcEventLoggerAdsProxy.Native.Interop.dll` | `TwinCAT\3.1\Components\TcEventloggerV2\v1xx\` | Only when built with `-p:BundleBeckhoffDlls=true` (opt-in); default build resolves it at runtime from the TwinCAT installation — see `SECURITY.md` for the runtime-loading seam and what still needs live verification |
| `TcEventLoggerAdsProxy.dll` (native) | same directory as above | Same as above |
| `TcHmiAutomation.dll` (TE2000 HMI Automation Interface) | `TwinCAT\Functions\TE2000-HMI-Engineering\bin\` | Only when built with `-p:BundleBeckhoffDlls=true` (opt-in); the default build references it at compile time only and `BeckhoffAssemblyResolver` loads it at runtime from that directory. Releases up to 1.4.0 still shipped a copy of this file; later releases do not |
| `TcTypeSystem.dll` (native, x64) | `TwinCAT\Functions\TE2000-HMI-Engineering\bin\TmcParser\win-x64\native\`, else `TwinCAT\CommonComServer\TcTypeSystem\2.8\x64\` | No — not referenced at build time; loaded at runtime from the TwinCAT installation by `NativeLibrary.Load` (`TcTypeSystemNative.cs`, #161) only when `twincat_hmi_enable_runtime_tmc` converts a TMC file |

Anyone building or running ptc-xae-mcp must have a licensed TwinCAT 3.1
XAE installation of their own; this project neither distributes nor
sublicenses Beckhoff's software.

## Test fixtures

- `tests/Fixtures/PtcXaeMcpFixture` is a TwinCAT solution generated by
  `tests/Fixtures/create_fixture.py`. It references Beckhoff standard
  libraries by name only; no library binaries are included.
- `tests/Fixtures/PtcXaeMcpHmiFixture` is a TwinCAT HMI project expanded
  from the TE2000 "TwinCAT HMI Project" starter template by
  `tests/Fixtures/create_hmi_fixture.py`, the way the XAE's "Add New Project"
  does it. The template files it contains (themes, configuration, views)
  are the Beckhoff-authored starting point of every TE2000 project. The
  Beckhoff company logo the template shows is **not** included; the script
  writes a neutral placeholder (`Images/Fixture_Logo.svg`) instead. The
  Roboto Condensed web fonts under `Fonts/` ship with that template and are
  licensed under the Apache License 2.0 (Copyright Christian Robertson /
  Google). No HMI framework packages are included; the XAE restores them
  from the local Beckhoff NuGet feed when the fixture is opened.

## Development tools (not part of the product)

The following live under `tools/` and are used only by maintainers; none
of them is compiled into or shipped with ptc-xae-mcp:

| Tool | Origin | License |
|---|---|---|
| `tools/ads_read.py` | uses the Python package **pyads** (not included) | MIT |
| `tools/convert_favicon.py` | uses **CairoSVG** and **Pillow** (not included) | LGPL-3.0 / MIT-CMU |

## Trademarks

Beckhoff®, TwinCAT®, TwinSAFE® and EtherCAT® are registered trademarks of,
and licensed by, Beckhoff Automation GmbH, Germany; EtherCAT® is also
patented technology. Claude is a trademark of Anthropic PBC. Microsoft,
Windows, Visual Studio and .NET are trademarks of the Microsoft group of
companies. This project is not affiliated with or endorsed by any of them.
See [TRADEMARKS.md](TRADEMARKS.md) for the Pneutronic XAE MCP / Pneutronic
trademark policy.
