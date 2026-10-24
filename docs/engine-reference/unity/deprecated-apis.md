# Unity — Deprecated / Removed APIs

Last verified: 2026-10-24. Source: upgrade guides in `VERSION.md`. "Don't use X → use Y".

| Don't use | Use instead | Since |
|-----------|-------------|-------|
| `Object.FindObjectsOfType` | `Object.FindObjectsByType(..., FindObjectsSortMode.None)` | 6.0 (obsolete) |
| `Object.FindObjectOfType` | `Object.FindFirstObjectByType` or `Object.FindAnyObjectByType` | 6.0 (obsolete) |
| `GraphicsFormat.DepthAuto` / `ShadowAuto` / `VideoAuto` | `GraphicsFormat.None` for depth-only RTs (+ `shadowSamplingMode = CompareDepths` for shadow maps) | 6.0 (compile error) |
| `LightingSettings.filteringGaussRadiusAO/Direct/Indirect` (int) | `filteringGaussianRadiusAO/Direct/Indirect` (float) | 6.0 |
| `RenderPipelineEditorUtility.FetchFirstCompatibleTypeUsingScriptableRenderPipelineExtension` | `GetDerivedTypesSupportedOnCurrentPipeline` (returns all types) | 6.0 |
| `CustomEditorForRenderPipelineAttribute`, `VolumeComponentMenuForRenderPipelineAttribute` | `CustomEditor`, `VolumeComponentMenu` + `SupportedOnRenderPipelineAttribute` | 6.0 |
| `ExecuteDefaultAction`, `ExecuteDefaultActionAtTarget` (UI Toolkit) | `HandleEventBubbleUp` / `HandleEventTrickleDown` | 6.0 |
| `PreventDefault` (UI Toolkit) | `StopPropagation` | 6.0 |
| `UxmlTraits` / `UxmlFactory` | `[UxmlElement]` / `[UxmlAttribute]` | 6.0 |
| Auto Generate lighting setting | `Lightmapping.Bake` / `BakeAsync` | 6.0 (removed) |
| `_FORWARD_PLUS` shader keyword | `_CLUSTER_LIGHT_LOOP` | 6.1 |
| `Rigidbody.SetDensity` | `Rigidbody.mass` | 6.1 |
| URP `SetupRenderPasses` | render graph + `AddRenderPasses` | 6.2 |
| `VisualElement.transform` | set: `style.translate/rotate/scale`; read: `resolvedStyle.translate/rotate/scale` | 6.2 |
| URP Compatibility Mode / `RenderGraphSettings.enableRenderCompatibilityMode` (now read-only `false`) | URP Render Graph | 6.3 (removed) |
| `NetworkTransform.Update` override (NGO 2.X) | `NetworkTransform.OnUpdate` (non-authority only) | NGO 2.X, Unity 6.3 guide |
| `AccessibilityNode.selected` | `AccessibilityNode.invoked` | 6.3 |
| `AdditionalBakedProbes` (removed), `CustomBake` (obsolete) | `LightTransport.IProbeIntegrator` | 6.3 |
| `[SerializeField]` on non-fields | field only; `[field: SerializeField]` for auto-property backing fields | 6.3 (compile error) |
