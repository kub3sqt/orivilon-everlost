# Unity — Breaking Changes

Last verified: 2026-10-24. Source: the Unity upgrade guides listed in `VERSION.md`. Only desktop-relevant items are kept; Android, XR, Magic Leap, Facebook Instant Games and Ads items were left out on purpose (the project targets PC). Anything not on those pages is NOT SOURCEABLE here.

## 6.2 → 6.3 (6000.2 → 6000.3)

- **URP Compatibility Mode removed.** `RenderGraphSettings.enableRenderCompatibilityMode` is read-only and returns `false`. Convert to URP Render Graph first (on 6.0–6.2). The `URP_COMPATIBILITY_MODE` scripting define re-adds the code for conversion only and stops working in Unity 6.4.
- **`[SerializeField]` only on fields.** Applying it to properties, methods or types is now a compile error. For an auto-property backing field use `[field: SerializeField]`.
- **Netcode for GameObjects 1.X deprecated in favour of 2.X.** `NetworkTransform.Update` can no longer be overridden; use `NetworkTransform.OnUpdate` (called only on non-authority instances). For authority logic, use a `NetworkBehaviour` implementing `INetworkUpdateSystem` registered with `NetworkUpdateLoop`.
- **`Scene.handle` type changed `int` → `SceneHandle`** (implicit conversion to/from `int`; source compatible). Precompiled DLLs using it can throw `MissingFieldException` until recompiled.
- **`UnityEngine.Experimental.GlobalIllumination` `instanceID` fields became `EntityId`** (and were renamed `entityID`, except `LightDataGI.cookieID`); same DLL-recompile caveat.
- **Search Index Manager removed** (Window > Search > Index Manager). Use Preferences > Search > Indexing.
- **Legacy ETC compressor removed**; textures are re-encoded with the default ETC compressor.
- **Experimental lightmapping:** `AdditionalBakedProbes` removed, `CustomBake` obsolete; use `LightTransport.IProbeIntegrator`.
- **`UPM_NPM_CACHE_PATH` no longer supported**; use `UPM_CACHE_ROOT`.
- **Multiplayer Services package default is 2.0.0** (Multiplay Hosting removed; service shut down after 2026-03-31). Multiplayer Play Mode package default is 2.0.1 (remote instances in Play Mode scenarios removed).
- **UI Toolkit:** stricter USS parser — malformed USS now blocks the import; see **Unsupported Selector Action** in the importer Inspector.
- **Accessibility:** `AccessibilityRole` is a standard enum (not flags) and `AccessibilityRole`/`AccessibilityState` are `byte`; `AccessibilityNode.selected` → `invoked`.

## 6.1 → 6.2 (6000.1 → 6000.2)

- **URP `AfterRendering` injection point** now always runs after the final blit to the back buffer. To keep the old behaviour use `AfterRenderingPostProcessing` (backward compatible to Unity 6.0).
- **URP `SetupRenderPasses` deprecated.** Rewrite Scriptable Renderer Features with the render graph system and `AddRenderPasses`.
- **`VisualElement.transform` deprecated** (UI Toolkit) — see `deprecated-apis.md`.
- A set of shader APIs was deprecated; the full list is in the 6.2 release notes (https://unity.com/releases/editor/whats-new/6000.2.0), which were NOT fetched — NOT SOURCEABLE here.

## 6.0 → 6.1 (6000.0 → 6000.1)

- **`_FORWARD_PLUS` shader keyword replaced by `_CLUSTER_LIGHT_LOOP`.** Custom shaders must be updated.
- **`Rigidbody.SetDensity` deprecated**; use `Rigidbody.mass`.
- **Window menu:** Package Manager, Asset Store, Services, My Assets and Version Control moved into submenus; custom shortcuts to them break.
- **Windows default Auto Graphics API is DirectX12** for new projects; upgraded projects keep their previous setting.
- PVRTC support deprecated (mobile; not relevant on PC).

## 2022 LTS → 6.0 (6000.0)

- **Enlighten Baked GI removed**; scenes are switched to the Progressive Lightmapper. Enlighten precomputed Realtime GI remains up to Unity 6.
- **Auto Generate lighting removed**; use **Generate Lighting**, `Lightmapping.Bake` or `Lightmapping.BakeAsync`. Ambient probe and skybox Reflection Probe are no longer baked by default.
- **Light probes are brighter** (previously 94 % of expected energy).
- **`Object.FindObjectsOfType` / `FindObjectOfType` obsolete** → `FindObjectsByType` / `FindFirstObjectByType` / `FindAnyObjectByType`; pass `FindObjectsSortMode.None` for speed.
- **Physics torque:** `Rigidbody.AddForceAtPosition` and `AddExplosionForce` with `ForceMode.VelocityChange` or `ForceMode.Acceleration` now scale by the inertia tensor. To restore 2022 behaviour multiply by `body.mass` and switch to `ForceMode.Impulse` / `ForceMode.Force`.
- **`GraphicsFormat.DepthAuto`, `ShadowAuto`, `VideoAuto` obsolete** (compile errors).
- **Runtime-created Texture2D no longer follow Mipmap Limits** by default (opt-in via a constructor taking `MipmapLimitDescriptor`).
- **Metal:** `half`/`min16float`/`real` in buffers are now 32-bit floats in MSL — matters for raw `SetData` writes on macOS.
- **UI Toolkit:** `ExecuteDefaultAction`/`ExecuteDefaultActionAtTarget` deprecated → `HandleEventBubbleUp` / `HandleEventTrickleDown`; `PreventDefault` → `StopPropagation`; `UxmlElement`/`UxmlAttribute` replace `UxmlTraits`/`UxmlFactory`.
- **Assets/Create menu and built-in ScriptTemplates reorganised.**
- **Package cache:** `UPM_CACHE_PATH` and `UPM_NPM_CACHE_PATH` unsupported (`UPM_CACHE_ROOT`).
- Editor-bundled 7-Zip no longer supports zstandard.
- URP-specific upgrade details live in separate URP upgrade pages (NOT fetched — NOT SOURCEABLE here).
