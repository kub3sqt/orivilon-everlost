# Unity — Current Best Practices

Last verified: 2026-10-24. Only practices stated by the upgrade guides in `VERSION.md` are recorded; broader best practices for 6000.3 are NOT SOURCEABLE in this run (no "What's new" or manual page on performance/rendering was fetched). Do not add from memory — fetch and cite.

- **URP custom passes:** write Scriptable Renderer Features with the render graph system and `AddRenderPasses`; Compatibility Mode is removed in 6.3 (6.2 guide, 6.3 guide).
- **URP injection points:** to render into the intermediate texture rather than after the final blit, use `AfterRenderingPostProcessing` (6.2 guide).
- **Finding objects:** use `FindObjectsByType` / `FindFirstObjectByType` with `FindObjectsSortMode.None`; sorting by instance ID is slow for large lists (6.0 guide).
- **Netcode for GameObjects 2.X:** put non-authority interpolation customisation in `NetworkTransform.OnUpdate`; put authority-side logic in a `NetworkBehaviour` that implements `INetworkUpdateSystem` and registers with `NetworkUpdateLoop` (6.3 guide).
- **Serialization:** apply `[SerializeField]` only to fields; use `[field: SerializeField]` for auto-property backing fields (6.3 guide).
- **UI Toolkit custom controls:** `[UxmlElement]` / `[UxmlAttribute]` instead of `UxmlTraits`/`UxmlFactory` (6.0 guide).
- **Batch-mode and CI:** use `UPM_CACHE_ROOT` for a custom Package Manager cache location (6.0 and 6.3 guides).
