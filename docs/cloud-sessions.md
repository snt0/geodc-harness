# Cloud agent sessions

Cloud agent sessions run a coding agent in a disposable Ubuntu VM with fresh clones of this harness and of geoDC. The session's repository is the harness, with `snt0/geodc` attached as a second repository; the harness works on geoDC through `geodc/`, which links to that clone (Claude Code's session-start hook does this) or is cloned there. This page covers what every agent needs from that environment and what a Linux session can and cannot verify. Agent-specific setup lives in:

- [Claude Code](cloud-sessions-claude.md): environment settings, repository hooks, branch naming.
- [Codex](cloud-sessions-codex.md): environment settings.

## Toolchain

[`dev/cloud-setup.sh`](../dev/cloud-setup.sh) installs the .NET SDK into `/opt/dotnet`, PowerShell (`pwsh`) from Microsoft's apt feed, and `git-lfs`. Each agent runs it as the environment's setup script; where to paste it is in the agent pages.

The environment's network allowlist must reach `builds.dotnet.microsoft.com` in addition to the usual package managers (nuget.org, packages.microsoft.com, dot.net). The default trusted lists do not include the host that serves the .NET SDK binaries, and Ubuntu's own `dotnet-sdk-10.0` package is a 10.0.1xx SDK, below the `global.json` pin.

Only committed repository configuration and the environment reach a cloud session; user-level agent settings do not.

## Verification boundary on Linux

`Directory.Build.props` sets `EnableWindowsTargeting` on non-Windows hosts, so the whole solution compiles. Checked on Ubuntu 24.04 with the 10.0.4xx SDK:

| Runs on Linux | Windows only |
| --- | --- |
| `geoDC.Domain.Tests`, `geoDC.Application.Tests`, `geoDC.Infrastructure.Tests`, `geoDC.App.Core.Tests`, `geoDC.GeobaseImport.Tests`, `geoDC.UiDriver.Tests` (the UI driver's UIA-free logic), `geoDC.Proj.Tests` (against the committed `linux-x64` PROJ build), `geoDC.Reporting.Tests`, `geoDC.TestDataGenerator.Tests` | `geoDC.App.Tests`, `geoDC.App.UI.Tests`, `geoDC.App.Text.Generators.Tests` (Windows target frameworks), the app, the Sample gallery, the UI driver itself |

Treat cloud changes covered only by the Windows-only projects as compile-verified and confirm them in a Windows session.

`tools/geoDC.ReportCli` renders a job report from a `.gdcproj` on Linux (without charts), so report changes can be checked end to end in the cloud; it needs a bundled Typst and the LFS logo (`docs/reporting.md` > Headless reports).

The build provisions Typst by downloading a `typst/typst` GitHub release, which a cloud proxy may refuse. When that fetch fails, set `GeoDCSkipTypstFetch=true` so builds still succeed without a bundled Typst. Claude Code's session-start hook does this automatically.

Nerdbank.GitVersioning derives the build version from git history, so every build fails in a shallow clone ("Shallow clone lacks the objects required to calculate version height"). Run `git fetch --unshallow origin` before building, or set `NBGV_GitEngine=Disabled` when the history cannot be fetched; assembly versions then omit the git height. Claude Code's session-start hook does this automatically.
