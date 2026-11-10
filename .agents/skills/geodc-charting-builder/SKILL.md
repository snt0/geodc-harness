---
name: geodc-charting-builder
description: Build or refine geoDC charts across renderer-neutral C# contracts, data assembly, D3 ES-module renderers, WebView2 live hosting, deterministic SVG/report export, WPF chart configuration, browser preview, and contract or Node tests. Use for chart kinds, data/config fields, serialization, chart theme roles, interactions, or export behavior. Do not use the UI driver to inspect WebView2 chart internals.
---

# geoDC Charting Builder

Keep chart data, presentation config, live rendering, and report rendering aligned across the C#/JavaScript seam.

## Workflow

1. Read `geodc/docs/charting.md`, the target contract in `geodc/src/geoDC.Application/Charting/ChartContracts.cs`, and the matching renderer under `geodc/src/geoDC.App/ChartAssets/charts`.
2. Classify the change as authoritative data assembly, presentation config, renderer behavior, theme role, WPF control, interaction, live host, or static export.
3. Trace the same field or behavior through every affected layer before editing.
4. Update focused C# contract tests and pure JavaScript tests where applicable.
5. Use the browser preview for renderer inspection; reserve WPF checks for the outer host, setup rail, and readiness/interaction bridge.

## Invariants

- `IChartData` carries measurement state assembled from domain results; `IChartConfig` carries presentation state. Do not move view choices into persisted/domain results.
- Preserve the exact JSON contract: camel-case property names and fully lower-case enum tokens. JavaScript compares these tokens as strings.
- Copy authoritative statistics through `IChartDataAssembler`; renderers must not silently recompute domain results.
- Keep controls, explanatory text, and accuracy summaries in WPF. JavaScript modules are pure renderers and interactions.
- Live WebView2 and report SVG paths use the same renderer factory. A reportable chart must support `window.renderStatic`; VRF WebGL remains explicitly live-only.
- Map app theme tokens through `ChartPalette`; add every new renderer role to the deterministic `PRINT_PALETTE` fallback.
- Keep `charts/math.js` free of D3 and DOM dependencies so it remains directly testable under Node.

## Coherent config changes

When adding a config option, update the C# record/default, renderer defaults and draw path, viewmodel property/push/build/reset behavior, XAML setup control, and serialization test together. Use the existing suppression flag for bulk resets.

## Verification

- Contract shape: focused `ChartContractsTests` in `geodc/tests/geoDC.App.Tests`.
- Pure maths: `node --test geodc/src/geoDC.App/ChartAssets/charts/__tests__/math.test.js`.
- Assets: from `geodc/src/geoDC.App/ChartAssets`, run `npm run check`, `npm run lint`, and `npm run test` as applicable.
- Visual rendering: run `npm run dev` and inspect the deterministic `geodc/tools/preview` fixtures. The UI driver cannot see inside WebView2.
