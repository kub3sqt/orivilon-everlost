# Technical Preferences

<!-- project.yaml at the repo root is the machine-readable source of truth for
     engine, specialists, naming, performance, platform, and testing.framework.
     This file is the human-readable LEGACY FALLBACK: agents and skills resolve
     each key from project.yaml first and fall back here only when the
     project.yaml key is absent. /setup-engine dual-writes both.
     Forbidden patterns and allowed libraries are NOT migrated — they live only
     in this file. Populated by /setup-engine; updated as decisions are made. -->

## Engine & Language

- **Engine**: Unity 6000.3.10f1
- **Language**: C#
- **Rendering**: URP (com.unity.render-pipelines.universal 17.3.0)
- **Physics**: PhysX

## Input & Platform

<!-- Written by /setup-engine. Read by /ux-design, /ux-review, /test-setup, /team-ui, and /dev-story -->
<!-- to scope interaction specs, test helpers, and implementation to the correct input methods. -->

- **Target Platforms**: PC (Steam: Windows, macOS, Linux)
- **Input Methods**: Keyboard/Mouse
- **Primary Input**: Keyboard/Mouse
- **Gamepad Support**: Partial
- **Touch Support**: None
- **Platform Notes**: Project uses the legacy Input Manager (`activeInputHandler: 0`). Cursor, pause and movement blocking go through the existing systems.

## Naming Conventions

<!-- Engine defaults. Existing Everlost code (namespace Orivilon.*) keeps its current style; public fields are not renamed for style. -->
- **Classes**: PascalCase
- **Variables**: PascalCase public, _camelCase private
- **Signals/Events**: PascalCase `On<Event>`
- **Files**: PascalCase matching the class
- **Scenes/Prefabs**: PascalCase
- **Constants**: PascalCase or UPPER_SNAKE_CASE (UNVERIFIED — not sourceable from docs/engine-reference/unity/)

## Performance Budgets

- **Target Framerate**: [TO BE CONFIGURED]
- **Frame Budget**: [TO BE CONFIGURED]
- **Draw Calls**: [TO BE CONFIGURED]
- **Memory Ceiling**: [TO BE CONFIGURED]

## Testing

- **Framework**: [TO BE CONFIGURED] — com.unity.test-framework is not installed; existing checks in `Assets/Editor/Tests/` are editor scripts, not NUnit
- **Minimum Coverage**: [TO BE CONFIGURED]
- **Required Tests**: Balance formulas, gameplay systems, networking (if applicable)

## Forbidden Patterns

<!-- Add patterns that should never appear in this project's codebase -->
- [None configured yet — add as architectural decisions are made]

## Allowed Libraries / Addons

<!-- Add approved third-party dependencies here -->
- [None configured yet — add as dependencies are approved]

## Architecture Decisions Log

<!-- Quick reference linking to full ADRs in docs/architecture/ -->
- [No ADRs yet — use /architecture-decision to create one]

## Engine Specialists

<!-- Written by /setup-engine when engine is configured. -->
<!-- Read by /code-review, /architecture-decision, /architecture-review, and team skills -->
<!-- to know which specialist to spawn for engine-specific validation. -->

- **Primary**: unity-specialist
- **Language/Code Specialist**: unity-specialist (C# review — primary covers it)
- **Shader Specialist**: unity-shader-specialist (Shader Graph, HLSL, URP materials)
- **UI Specialist**: unity-ui-specialist (uGUI Canvas, TMP)
- **Additional Specialists**: unity-dots-specialist (Jobs/Burst/Collections used by terrain streaming; no ECS). unity-addressables-specialist omitted: no Addressables package installed.
- **Routing Notes**: Invoke primary for architecture and general C# review. Invoke the DOTS specialist for Jobs/Burst code. Invoke the shader specialist for rendering and visual effects. Invoke the UI specialist for interface implementation.

### File Extension Routing

<!-- Skills use this table to select the right specialist per file type. -->
<!-- If a row says [TO BE CONFIGURED], fall back to Primary for that file type. -->

| File Extension / Type | Specialist to Spawn |
|-----------------------|---------------------|
| Game code (.cs files) | unity-specialist |
| Shader / material files (.shader, .shadergraph, .mat) | unity-shader-specialist |
| UI / screen files (Canvas prefabs) | unity-ui-specialist |
| Scene / prefab / level files (.unity, .prefab) | unity-specialist |
| Native extension / plugin files (.dll, native plugins) | unity-specialist |
| General architecture review | Primary |
