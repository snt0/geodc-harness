# Agent prompts for the prerequisite issues

Three standalone briefs, one per prerequisite issue of #780. Each is written for a fresh session on `snt0/geodc`
with no memory of the investigation. Paste one prompt per session. They are independent and can run in parallel.

The investigation reports live under `docs/handoff/` on the branch of #803. Until that merges, a session on `dev`
has to fetch them, which each prompt says how to do.

---

## #805 — Remove the demo-project button

```
You are working in snt0/geodc. Read AGENTS.md first and follow it: branch from dev as
claude/issue-805-remove-demo-project, Conventional Commits, PR body per .github/pull_request_template.md,
record the user-visible change under "Unreleased" in CHANGELOG.md.

Task: issue #805, "Remove the demo-project button and the App reference to TestDataGenerator.Core". Read the
issue; it lists every file and line. The evidence report is docs/handoff/03-project-boundaries.md §3. If
docs/handoff does not exist on your checkout, run
  git fetch origin claude/lucid-heisenberg-5db2az
  git show origin/claude/lucid-heisenberg-5db2az:docs/handoff/03-project-boundaries.md
and read §3.

What to do, in this order:
1. Delete src/geoDC.App/Startup/DemoProjectGenerator.cs and IDemoProjectGenerator.cs and every CreateDemo /
   IsDemo / isDemo path the issue lists in Startup/, Services/ProjectFileWorkflowService.cs and the two
   Startup templates. Remove the tools/geoDC.TestDataGenerator.Core ProjectReference from geoDC.App.csproj.
2. In src/geoDC.App/Strings/launch.yaml, remove the createDemo and demo* keys and collapse the seven
   `match: bool` blocks whose true case is the demo into plain entries. Generated members change from
   methods to properties; fix the callers. Keep the remaining copy identical; read docs/copy-style.md
   before rewording anything.
3. Remove the Demo preset from tools/geoDC.TestDataGenerator.Core/Scenarios/ScenarioPresets.cs, the CLI help
   and ScenarioPresetsTests. Do not touch the Probe preset or dev/seed-probe-fixture.ps1.
4. Delete tests/geoDC.App.Tests/Startup/DemoProjectCreationTests.cs; fix the IDemoProjectGenerator substitute
   in ProjectSessionCleanupOrderTests.cs. tests/geoDC.App.Tests keeps its own TestDataGenerator.Core
   reference; leave it.
5. Rewrite docs/test-data-generator.md lines 30 and 37-51; replace the Unreleased CHANGELOG entry for #189
   with a "Removed" line.

Verify on Linux: dotnet build src/geoDC.App and tests/geoDC.App.Tests in Release with
-p:GeoDCStrictCodeStyle=true; dotnet test tests/geoDC.TestDataGenerator.Tests and
tests/geoDC.Infrastructure.Tests (the string-resource convention test must pass: every launch.yaml entry
still used). grep -rn TestDataGenerator src/geoDC.App must return nothing. A Windows run of
geoDC.App.Tests/Startup goes under "Still to verify" in the PR.

Before opening the PR, run the geodc-branch-wrap-up skill. The PR says "Closes #805" and "Part of #780".
After it is open, leave one comment on #461 saying the demo project (#189) is removed by this PR and that
presets remain the single source for the probe fixture and test scenarios. Do not touch AutomationId lines
elsewhere: PR #821 is renaming them.
```

---

## #806 — Emit generated text files and move literal popup titles into YAML

```
You are working in snt0/geodc. Read AGENTS.md first and follow it: branch from dev as
claude/issue-806-generated-text-visibility, Conventional Commits, PR body per
.github/pull_request_template.md. Read docs/string-resources.md and docs/copy-style.md before touching
YAML.

Task: issue #806. Evidence: docs/handoff/04-discoverability-audit.md §2, §5 (#3-#5) and §6. If docs/handoff
is absent, fetch it with
  git fetch origin claude/lucid-heisenberg-5db2az
  git show origin/claude/lucid-heisenberg-5db2az:docs/handoff/04-discoverability-audit.md

What to do:
1. In src/geoDC.App/geoDC.App.csproj add EmitCompilerGeneratedFiles=true and
   CompilerGeneratedFilesOutputPath=$(BaseIntermediateOutputPath)Generated, exactly as
   src/geoDC.App.UI/geoDC.App.UI.csproj lines 6-7 do. Build, confirm the .g.cs files appear under
   src/geoDC.App/obj/, and confirm no CS0101 duplicate-definition error.
2. Move the C# literal popup titles and subtitles into YAML:
   src/geoDC.App/Popups/WorkflowPopupDefinitions.cs lines 47-48, 163-164, 191-192, 247;
   Popups/FingerprintPopupDefinition.cs 18-19; ViewModels/Setup/ProjectMetadataEditorPopupDefinition.cs 16;
   Startup/ProjectSelectorPopupDefinition.cs 15. Put each under the file for its area (popups.yaml for the
   shared Survey popups, setup.yaml, launch.yaml, shell.yaml for the fingerprint popup if that is where its
   other copy lives); an interpolated subtitle becomes a format entry with a typed placeholder. On-screen
   text must not change.
3. Extend the convention tests under tests/geoDC.Infrastructure.Tests/Conventions/ so a string literal
   assigned to Title or SubTitle inside a popup definition in src/geoDC.App fails the build's test run.
   Follow the style of XamlLiteralTextConventionTests.cs. Make sure it passes after step 2 and fails if you
   temporarily put a literal back.
4. Fix the stale comment at src/geoDC.App/Converters/AppConverters.cs lines 17-19 (the XAML prefix is snt:,
   not conv:).
5. In docs/string-resources.md add one short paragraph saying where generated files land after a build and
   that grep and Visual Studio Find in Files can search them.

Verify on Linux: build src/geoDC.App in Release with -p:GeoDCStrictCodeStyle=true; dotnet test
tests/geoDC.Infrastructure.Tests; grep -rn "No job selected" src/geoDC.App/obj hits generated classes.
No CHANGELOG entry: nothing user-visible changes. Windows check of the seven popup titles goes under
"Still to verify".

Run the geodc-branch-wrap-up skill before opening the PR. The PR says "Closes #806" and "Part of #780".
```

---

## #807 — Solution hygiene and solution filters

```
You are working in snt0/geodc. Read AGENTS.md and README.md first and follow AGENTS.md: branch from dev as
claude/issue-807-solution-hygiene, Conventional Commits, PR body per .github/pull_request_template.md.

Task: issue #807. Evidence: docs/handoff/03-project-boundaries.md §7 and §8. If docs/handoff is absent:
  git fetch origin claude/lucid-heisenberg-5db2az
  git show origin/claude/lucid-heisenberg-5db2az:docs/handoff/03-project-boundaries.md

What to do:
1. geoDC.slnx lists three files that do not exist: GlobalUsings.Library.cs, GlobalUsings.Wpf.cs (around
   lines 29-30) and StaTestDispatcher.cs (around line 50). Remove them. Check every remaining <File> entry
   exists.
2. dotnet-tools.json restores jetbrains.resharper.globaltools (jb). Confirm with grep that no script, hook,
   workflow or doc invokes it (docs/coding-style.md line 8 mentions it in prose). Remove the tool and
   reword that sentence.
3. Add four solution filters at the repository root, with the project lists from the report's §7 table and
   each filter's transitive dependencies: geoDC.Core.slnf, geoDC.App.slnf, geoDC.Toolkit.slnf,
   geoDC.Tools.slnf. Confirm `dotnet build <filter>` succeeds for each on Linux (the SDK pinned in
   global.json supports .slnf over .slnx; if a filter cannot build on Linux because of a Windows-only
   project, say so in the PR and show it restores). Add a row to README's repository-layout table naming
   them and what each is for. CI keeps building geoDC.slnx; do not change workflows.
4. README "Getting started": add the prerequisites a Visual Studio developer is not told today: Git LFS
   (all *.svg are LFS objects; without it logos are blank and the report render fails; the Husky hooks exit
   2 without it), PowerShell 7 (dev/format-xaml.ps1 and dev/fetch-typst.ps1), that the first build downloads
   Typst and GeoDCSkipTypstFetch=true skips it, and that Directory.Build.rsp makes dotnet build silent on
   success. Keep it to a short list; link docs/reporting.md and docs/proj-native.md rather than explaining.

Verify: dotnet build geoDC.slnx on Linux; each filter builds or restores as described; dotnet tool restore
no longer downloads the ReSharper CLI. Record under "Still to verify": open each .slnf in Visual Studio on
Windows and confirm no missing items.

Run the geodc-branch-wrap-up skill before opening the PR. The PR says "Closes #807" and "Part of #780".
```
