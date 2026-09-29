# PICO MCP Extensions

PICO XR feature construction APIs for Unity MCP agents. Idempotent, non-destructive, ambiguity-aware.

## Overview

This Unity package exposes PICO XR building blocks as MCP (Model Context Protocol) tools, enabling AI agents (e.g. Unity AI Assistant) to programmatically configure XR scenes for PICO devices. It is designed to be used together with the **PICO Unity Integration SDK 6.1.x** and aligns its dependency baseline with that SDK (XR Interaction Toolkit **3.x**).

**Package name:** `com.bytedance.pico.mcp-extensions`  
**Version:** 0.1.0-alpha.1<br>
**Unity:** 6000.0+  
**Author:** ByteDance PICO

## Requirements

| Dependency | Minimum Version |
|---|---|
| com.unity.xr.core-utils | 2.3.0 |
| com.unity.xr.interaction.toolkit | 3.4.0 |
| com.unity.inputsystem | 1.18.0 |
| Unity AI Assistant (Unity.AI.MCP.Editor) | 2.x |
| com.bytedance.pico.xr | 6.1.0 |

## Features

Eight XR building blocks:

| Block | Description |
|---|---|
| **VST** | Video See-Through (passthrough) - configures camera for transparent background and adds `PXR_CameraEffectBlock` |
| **Controller** | Mounts PICO controller visual models on Left/Right hand anchors |
| **Controller Haptics** | Adds one XR-Origin haptics manager with named impulse, AudioClip-buffer, and PHF-buffer effects; gameplay code or UnityEvents choose when to invoke them |
| **Locomotion** | Enables XRI locomotion subtree with fine-grained presets (Move, Turn, Teleportation, GrabMove, Climb, Gravity, Jump) |
| **Spatial Mesh** | Configures `PXR_SpatialMeshManager` with auto-detected MeshPrefab (depends on VST) |
| **Plane Detection** | Configures PICO SensePack plane detection via a bundled `PXR_PlaneDetectionManager` driver (depends on VST; PICO-native runtime only) |
| **Hand** | Enables PICO hand tracking (virtual hands) plus an XRI hand-interactor rig so a pinch can drive grab |
| **Grab** | Object pick-up & drag; ensures the scene `XRInteractionManager` broker and can upgrade a target object into a grabbable |

Additionally, a **Package** tool manages Unity packages and samples (install / remove / update / import samples / query resolvable version).

### Runtime support

Most building blocks compile and configure correctly under both PICO XR runtimes:

- **PICO-native runtime** (`ENABLE_PICO_XR_SDK`)
- **OpenXR runtime** (`ENABLE_PICO_OPENXR_SDK`) — VST enables the PICO `PassthroughFeature`; Spatial Mesh forces MultiPass rendering and enables the `PICOSpatialMesh` feature; Hand enables the Unity XR Hands models plus the PICO hand-tracking / hand-interaction OpenXR features.

> **Plane Detection is PICO-native only.** PICO ships no plane-detection OpenXR feature, so under the OpenXR runtime the plane provider is never created and the block is a no-op.
> **Controller Haptics is PICO-native only.** Its runtime manager uses the current
> `PXR_Input.SendHapticImpulse`, `SendHapticBuffer`, and buffer-lifecycle APIs; the
> package still compiles under OpenXR, but haptics attach/upsert actions report unsupported.

## Architecture

```
Editor/
  PXR_MCP_Common.cs        # Shared helpers: XR Origin lifecycle, module visibility
  PXR_MCP_Features.cs      # Building block implementations (VST, Controller, Locomotion, SpatialMesh, Plane, Hand, Grab)
  PXR_MCP_Haptics.cs       # Editor lifecycle for the XR-Origin haptics manager and named effects
  PXR_MCP_PackageOps.cs    # Package Manager operations (add, remove, samples)
  Tools/
    PXR_MCP_Tools.cs       # MCP tool surface ([McpTool] entry points)
    PXR_MCP_Result.cs      # Uniform result envelope for LLM consumption
Runtime/
  PXR_MCP_HapticsManager.cs # Runtime bridge to current PICO impulse and buffer APIs
```

**Layer 1 (Editor):** Plain C# static methods for building-block operations.
**Layer 2 (Tools):** `[McpTool]`-annotated methods that wrap Layer 1 and return `PXR_MCP_Result` envelopes.
The controller-haptics block additionally ships a runtime manager so gameplay
code and UnityEvents can invoke named effects in a player build.

## MCP Tools

| Tool | Actions | Description |
|---|---|---|
| `pico_xr_vst` | Enable, Disable, Status | Manage Video See-Through |
| `pico_xr_controller` | Enable, Disable, Status | Manage PICO controller models |
| `pico_xr_haptics` | Attach, UpsertEffect, RemoveEffect, Remove, Status | Manage one PICO-native haptics manager and its named effects without choosing gameplay triggers |
| `pico_xr_locomotion` | Enable, Disable, Configure, Status | Manage locomotion with preset flags |
| `pico_xr_spatial_mesh` | Enable, Disable, Status | Manage spatial mesh (requires VST) |
| `pico_xr_plane` | Enable, Disable, Status | Manage PICO plane detection (requires VST; PICO-native runtime only) |
| `pico_xr_hand` | Enable, Disable, Status | Manage PICO hand tracking (virtual hands) |
| `pico_xr_grab` | Enable, Disable, Status, MakeGrabbable | Manage grab pick-up & drag; `make_grabbable` upgrades a target object |
| `pico_xr_package` | List, Info, Add, Remove, Update, ListSamples, ImportSample, Resolvable | Unity Package Manager operations; `resolvable` is a read-only query for the latest-compatible version |
| `pico_xr_status` | (none) | Aggregate snapshot of all blocks |

## Controller haptics

`pico_xr_haptics` attaches one `PXR_MCP_HapticsManager` directly to the existing
agent XR Origin. It does not require Controller GameObjects and never creates an
XR Origin by itself. The manager stores multiple named effects; every effect has
one source type and may target left, right, or both controllers. A `both` effect
uses independent left/right parameters, assets, and runtime source IDs. Use two
named effects when the two hands need different source types.

Supported serialized effect types use current PICO APIs:

```csharp
PXR_Input.SendHapticImpulse(vibrateType, amplitude, durationMs, frequencyHz);
PXR_Input.SendHapticBuffer(vibrateType, audioClip, channelFlip, ref sourceId, cacheType);
PXR_Input.SendHapticBuffer(vibrateType, phfText, channelFlip, amplitudeScale, ref sourceId);
```

Raw PCM is a code-level entry point because arbitrary `float[]` data is not a
useful Inspector field. The manager exposes UnityEvent-friendly `Send`,
`StartEffect`, `Pause`, `Resume`, `UpdateEffect`, `Stop`, `StopAndClear`,
`StopAll`, and amplitude methods, plus `Try*` counterparts for code that needs a
success value. Serialized callbacks expose requested, sent, source-created,
started, paused, resumed, updated, stopped, and failed states. The SDK provides
no reliable completion callback, so this component does not claim an
`OnCompleted` event.

The parameterless cached-start and update methods are named `StartEffect()` and
`UpdateEffect()` so Unity does not invoke them automatically as MonoBehaviour
`Start` / `Update` lifecycle messages. Named `StartEffect(string)` and
`UpdateEffect(string)` overloads remain available for UnityEvents and business
code.

The manager deliberately does not subscribe to XR Interaction Toolkit or other
gameplay events. Application code or a user-selected UnityEvent owns all trigger
timing. Deprecated `StartVibrateBy*`, old Haptic Stream APIs, Unity XR/OpenXR
substitutes, and the separate advanced parametric API are not used.

Controller haptics currently require the PICO-native runtime
(`ENABLE_PICO_XR_SDK`). OpenXR projects continue to compile, but attach and
configure report unsupported; status and cleanup remain available. Editor
execution cannot prove physical vibration; validate the final behavior on a
connected PICO device.

## Design Principles

- **Idempotent:** Re-running any operation with the same arguments is a safe no-op.
- **Non-destructive:** Never destroys or deactivates foreign (non-agent-owned) XR Origins.
- **Module isolation:** Enabling one block does not implicitly enable unrelated modules. Initial-create hides Controller and Locomotion so VST stays clean.
- **No hardcoded versions:** XRI paths and types are resolved dynamically via `PackageInfo` and reflection.
- **Undo-safe:** All scene modifications go through Unity's Undo system.

## Installation

Add this package to your Unity project via the Package Manager:

1. Open **Window > Package Manager**
2. Click **+** > **Add package from disk...** (or add to `Packages/manifest.json`)
3. Ensure XRI Starter Assets sample is imported (required for XR Origin prefab)

## License

Copyright (c) 2015-2022 PICO Technology Co., Ltd. All rights reserved. See [LICENSE.md](LICENSE.md) for details.
