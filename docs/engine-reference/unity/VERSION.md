# Unity — Version Reference

Last verified: 2026-10-24

| Field | Value |
|-------|-------|
| **Engine Version** | 6000.3.10f1 (Unity 6.3 LTS) |
| **Project Pinned** | 2026-10-24 |
| **Installed at pin time** | 6000.3.10f1 — MATCH. Hub directory `/Applications/Unity/Hub/Editor/6000.3.10f1/` and its `-version` output both report 6000.3.10f1; `ProjectSettings/ProjectVersion.txt` is the same. (6000.5.1f1 is also installed; it is not the pin.) |
| **LLM Knowledge Cutoff** | Per the `/setup-engine` coverage table: May 2025, Unity coverage up to ~2022.3 LTS |
| **Risk Level** | HIGH — 6000.3 is beyond the table's Unity coverage (2022.3). Consult this directory before suggesting Unity APIs. |

## Post-cutoff timeline

| Version | Status in this reference |
|---------|--------------------------|
| 2022.3 LTS | Baseline of the model's Unity knowledge (per the table). |
| 6000.0 (Unity 6.0) | Upgrade guide read — see `breaking-changes.md`. |
| 6000.1 (Unity 6.1) | Upgrade guide read. |
| 6000.2 (Unity 6.2) | Upgrade guide read. |
| 6000.3 (Unity 6.3 LTS) | Upgrade guide read. Released 2025-12-04 with two years of LTS support (from the search-result summary of Unity's announcement; the announcement page itself was not fetched). |

Release dates for 6000.0, 6000.1 and 6000.2: NOT SOURCEABLE — not stated on the upgrade-guide pages fetched.

## Project packages (from `Packages/manifest.json`, read 2026-10-24)

`com.unity.render-pipelines.universal` 17.3.0 · `com.unity.netcode.gameobjects` 2.12.0 · `com.unity.burst` 1.8.28 · `com.unity.collections` 2.6.7 · `com.unity.mathematics` 1.3.3. No Addressables and no `com.unity.test-framework` package. `activeInputHandler: 0` (legacy Input Manager).

## Not covered (NOT SOURCEABLE)

Package-level release notes (URP 17.x, NGO 2.x, Burst, Collections), per-subsystem behaviour (PhysX defaults, voxel/mesh-API changes, UI Toolkit vs uGUI performance) and the 6000.3 patch-level release notes for 6000.3.1–6000.3.10 were not fetched in this run. Do not fill them from training data; fetch the page and add it here.

## Sources (fetched 2026-10-24)

- https://docs.unity.com/en-us/engine/6000.3/manual/upgrade-guides/upgrade-guide-unity63.md
- https://docs.unity.com/en-us/engine/6000.3/manual/upgrade-guides/upgrade-guide-unity62.md
- https://docs.unity.com/en-us/engine/6000.3/manual/upgrade-guides/upgrade-guide-unity61.md
- https://docs.unity.com/en-us/engine/6000.3/manual/upgrade-guides/upgrade-guide-unity6.md
