# PICO MCP Extensions

PICO XR feature construction APIs for Unity MCP agents. Idempotent, non-destructive, ambiguity-aware.

## Overview

This Unity package exposes PICO XR building blocks as MCP (Model Context Protocol) tools, enabling AI agents (e.g. Unity AI Assistant) to programmatically configure XR scenes for PICO devices. It is designed to be used together with the **PICO Unity Integration SDK 6.0.x** and aligns its dependency baseline with that SDK (XR Interaction Toolkit **3.x**).

**Package name:** `com.bytedance.pico.mcp-extensions`  
**Version:** 0.0.4  
**Unity:** 6000.0+  
**Author:** ByteDance PICO

## Requirements

| Dependency | Minimum Version |
|---|---|
| com.unity.xr.core-utils | 2.3.0 |
| com.unity.xr.interaction.toolkit | 3.4.0 |
| com.unity.inputsystem | 1.18.0 |
| Unity AI Assistant (Unity.AI.MCP.Editor) | 2.x |
| com.bytedance.pico.xr | 6.0.0 |

## Features

Four XR building blocks, each with Enable / Disable / Status semantics:

| Block | Description |
|---|---|
| **VST** | Video See-Through (passthrough) - configures camera for transparent background and adds `PXR_CameraEffectBlock` |
| **Controller** | Mounts PICO controller visual models on Left/Right hand anchors |
| **Locomotion** | Enables XRI locomotion subtree with fine-grained presets (Move, Turn, Teleportation, GrabMove, Climb, Gravity, Jump) |
| **Spatial Mesh** | Configures `PXR_SpatialMeshManager` with auto-detected MeshPrefab (depends on VST) |

Additionally, a **Package** tool manages Unity packages and samples (install / remove / update / import samples).

## Architecture

```
Editor/
  PXR_MCP_Common.cs        # Shared helpers: XR Origin lifecycle, module visibility
  PXR_MCP_Features.cs      # Building block implementations (VST, Controller, Locomotion, SpatialMesh)
  PXR_MCP_PackageOps.cs    # Package Manager operations (add, remove, samples)
  Tools/
    PXR_MCP_Tools.cs       # MCP tool surface ([McpTool] entry points)
    PXR_MCP_Result.cs      # Uniform result envelope for LLM consumption
```

**Layer 1 (Editor):** Plain C# static methods + Unity MenuItems for manual validation.  
**Layer 2 (Tools):** `[McpTool]`-annotated methods that wrap Layer 1 and return `PXR_MCP_Result` envelopes.

## MCP Tools

| Tool | Actions | Description |
|---|---|---|
| `pico_xr_vst` | Enable, Disable, Status | Manage Video See-Through |
| `pico_xr_controller` | Enable, Disable, Status | Manage PICO controller models |
| `pico_xr_locomotion` | Enable, Disable, Configure, Status | Manage locomotion with preset flags |
| `pico_xr_spatial_mesh` | Enable, Disable, Status | Manage spatial mesh (requires VST) |
| `pico_xr_package` | List, Info, Add, Remove, Update, ListSamples, ImportSample | Unity Package Manager operations |
| `pico_xr_status` | (none) | Aggregate snapshot of all blocks |

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

## Manual Testing (MenuItems)

> **Note:** The **PICO MCP** menu bar is hidden by default. It is guarded by the
> `PICO_MCP_SHOW_MENU` scripting define symbol (default: **off / false**). The MCP
> tool surface (`[McpTool]` methods) and all underlying C# APIs work regardless of
> this symbol — it only controls whether the manual-testing menu items appear.
>
> To **show** the menu (set to true), add `PICO_MCP_SHOW_MENU` to your project's
> scripting define symbols:
>
> - **Editor:** *Edit > Project Settings > Player > Other Settings > Scripting Define Symbols*, add `PICO_MCP_SHOW_MENU`, then Apply.
> - **Or** edit `Packages/manifest.json` / your `.asmdef` `defineConstraints`, or add it to `csc.rsp` (`-define:PICO_MCP_SHOW_MENU`).
>
> To **hide** the menu again (default), remove the symbol.

When `PICO_MCP_SHOW_MENU` is defined, all building blocks are accessible via the Unity menu:

- **PICO MCP > VST > Ensure / Remove**
- **PICO MCP > Controller > Ensure / Remove**
- **PICO MCP > Locomotion > Enable / Disable / Configure...**
- **PICO MCP > Spatial Mesh > Ensure / Remove**
- **PICO MCP > Packages > ...**

## License

Copyright (c) 2015-2022 PICO Technology Co., Ltd. All rights reserved. See [LICENSE.md](LICENSE.md) for details.
