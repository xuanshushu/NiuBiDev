# Project Knowledge Base

This document stores project-specific knowledge, directory intent, module relationships, and migration context for `GitRepo/NB_FX`.

## Package Scope

`GitRepo/NB_FX` is a Unity package named `com.xuanxuan.nb.fx`.

Top-level modules:

- `NBShaders`: legacy production particle and FX shader package
- `NBShaders2`: next-generation shader package under active development
- `NBPostProcessing`: post-processing and fullscreen distortion pipeline
- `XuanXuanRenderUtility`: shared runtime/editor utility layer used by other modules

## Current Development Direction

Shader work currently has two valid tracks:

1. Maintain and extend `NBShaders` when production behavior needs to stay on the existing path.
2. Build and migrate features in `NBShaders2` when the work benefits from the newer modular editor architecture.

`NBShaders` is not obsolete. It is both the current production baseline and an active maintenance target.

`NBShaders2` should be treated as the modular evolution path, not as the only valid destination for every change.

## Directory Guide

### `NBShaders`

Current production implementation with tightly coupled shader, flags, and inspector logic.

- `Shader/ParticleBase.shader`: old main FX shader entry
- `Shader/HLSL/`: HLSL implementation details for the legacy shader
- `Editor/ParticleBaseGUI.cs`: large all-in-one custom inspector
- `Runtime/W9ParticleShaderFlags.cs`: packed flag and foldout bit definitions used by the old system

### `NBShaders2`

New shader path being developed from the legacy system.

- `Shader/NBShader.shader`: new shader entry, currently marked as development-only via `Hidden/Effects/NBShader2开发中`
- `Editor/NBShaderGUI.cs`: thin shader GUI entry point
- `Editor/ShaderGUIItems/`: modular editor blocks for `NBShaders2`
- `Runtime/`: currently minimal; runtime surface is not yet as mature as `NBShaders`

### `XuanXuanRenderUtility`

Shared foundation for editor GUI composition and common rendering helpers.

- `Editor/ShaderGUIItems/ShaderGUIRootItem.cs`: root orchestration for modular shader inspector UIs
- `Runtime/ShaderFlagsBase.cs`: base abstraction for packed shader flags
- `Editor/ShaderGUIHelper.cs` and related files: common inspector helpers reused across shader editors
- `Shader/HLSL/`: shared shader utility code and bundled noise functions

### `NBPostProcessing`

Separate post-processing stack with its own runtime passes and shaders.

It is related to the broader FX package but is not the primary target when working on `NBShaders2`.

## Module Relationships

`NBShaders2` currently depends on the same conceptual flag model as `NBShaders`, while moving the inspector implementation onto reusable classes from `XuanXuanRenderUtility`.

Key observed relationships:

- `NBShaders/Runtime/W9ParticleShaderFlags.cs` defines many packed material flags and foldout bits
- `NBShaders2/Editor/NBShaderGUI.cs` creates a `NBShaderRootItem` instead of embedding all inspector logic directly
- `NBShaders2/Editor/ShaderGUIItems/ModeBigBlockItem.cs` and `BaseOptionBigBlockItem.cs` are early examples of the modular GUI pattern
- `XuanXuanRenderUtility/Editor/ShaderGUIItems/ShaderGUIRootItem.cs` provides the reusable root lifecycle for those modular blocks

This means `NBShaders2` is not a clean-room rewrite. It is a staged migration that still inherits assumptions and naming from the old `NBShaders` ecosystem.

## ShaderFlag System

One of the most important project-specific systems is the packed ShaderFlag protocol.

This is not just a convenience bitmask. It is a shared contract across C#, material properties, shader defaults, inspector state, and HLSL decoding.

### Core Files

- `XuanXuanRenderUtility/Runtime/ShaderFlagsBase.cs`
- `NBShaders/Runtime/W9ParticleShaderFlags.cs`
- `NBShaders/Shader/HLSL/EffectFlags.hlsl`
- `NBShaders/Shader/HLSL/ParticlesUnlitInputNew.hlsl`
- `NBShaders/Editor/ParticleBaseGUI.cs`
- `XuanXuanRenderUtility/Editor/ShaderGUIHelper.cs`
- `NBShaders2/Shader/NBShader.shader`

### What `ShaderFlagsBase` Actually Does

`ShaderFlagsBase` is the common C# abstraction for packed integer-based shader state.

It provides:

- `SetFlagBits`
- `ClearFlagBits`
- `CheckFlagBits`
- support for both `Material` and `MaterialPropertyBlock`
- indexed storage, so one derived type can manage multiple packed integers rather than a single flag field

This base class is reused outside `NBShaders` as well, for example by `NBPostProcessFlags`, so the pattern is a package-level convention, not a one-off implementation detail.

### What `W9ParticleShaderFlags` Adds

`W9ParticleShaderFlags` is the concrete packed protocol for the main particle shader family.

It maps multiple storage slots by index:

- index `0`: `_W9ParticleShaderFlags`
- index `1`: `_W9ParticleShaderFlags1`
- index `2`: `_W9ParticleShaderWrapFlags`
- index `3`: `_W9ParticleShaderGUIFoldToggle`
- index `4`: `_W9ParticleShaderGUIFoldToggle1`
- index `5`: `_W9ParticleShaderGUIFoldToggle2`
- index `6`: `_W9ParticleShaderColorChannelFlag`
- index `7`: `_W9ParticleShaderPNoiseBlendFlag`

This means the system already goes beyond simple feature toggles. It also stores:

- editor foldout persistence
- wrap-mode encoding
- UV mode selection
- color channel selection
- custom data source mapping
- procedural-noise blend mode selection

### What `EffectFlags.hlsl` Represents

`EffectFlags.hlsl` is the shader-side mirror of the C# protocol.

It contains:

- the bit definitions that correspond to `W9ParticleShaderFlags`
- helper decoding functions such as `GetCustomData`
- UV routing helpers such as `GetUVByUVMode`
- p-noise blend decoding
- assumptions about wrap-mode bit layout and packed component encoding

In practice, this file is a protocol definition file. If C# and HLSL drift apart here, shader behavior becomes silently wrong.

### Where the Flags Are Bound in Shader Code

`ParticlesUnlitInputNew.hlsl` declares the packed fields in `UnityPerMaterial`, including:

- `_W9ParticleShaderFlags`
- `_W9ParticleShaderFlags1`
- `_W9ParticleShaderWrapFlags`
- `_W9ParticleCustomDataFlag0..3`
- `_UVModeFlag0`
- `_UVModeFlagType0`
- `_W9ParticleShaderColorChannelFlag`
- `_W9ParticleShaderPNoiseBlendFlag`

It also provides local readers such as:

- `CheckLocalFlags`
- `CheckLocalFlags1`
- `CheckLocalWrapFlags`

This makes the HLSL side directly dependent on exact property names and bit layout.

### Where the Flags Are Used in GUI

In the legacy inspector path:

- `ParticleBaseGUI.cs` constructs `W9ParticleShaderFlags`
- `ShaderGUIHelper.cs` reads and writes foldout bits through `ShaderFlagsBase`
- many visible GUI states are not purely editor-only abstractions; they are persisted through packed material integers

In the new inspector path:

- `NBShaders2/Editor/NBShaderGUI.cs` still uses `W9ParticleShaderFlags`
- modular items such as `ModeBigBlockItem` rely on the same packed protocol for state and side effects

### Important Consequence

This system should be treated as a protocol, not as a local implementation detail.

When changing any part of it, think in terms of compatibility across:

- C# constants
- HLSL macros
- shader property declarations
- default material values in `.shader`
- inspector write logic
- runtime `MaterialPropertyBlock` logic
- existing serialized materials

### Common ShaderFlag Pitfalls

- changing a C# bit constant without updating `EffectFlags.hlsl`
- adding a new packed behavior but forgetting shader property declaration or default value
- editing foldout bit allocations and accidentally breaking `AnimBool` index assumptions
- changing wrap-mode packing without updating `CheckLocalWrapFlags`
- changing custom-data packing without updating `GetCustomData`
- changing UV mode packing without updating both the editor-side writer and HLSL-side decoder
- assuming a toggle is only UI state when it is actually persisted in packed flags

## Legacy vs New Architecture

### `NBShaders`

Characteristics:

- giant shader property surface
- large inspector class with many responsibilities
- behavior coordinated through packed bit flags and direct material mutations
- production behavior currently lives here
- feature growth historically happened inside one main system

Typical reference files:

- `NBShaders/Shader/ParticleBase.shader`
- `NBShaders/Editor/ParticleBaseGUI.cs`
- `NBShaders/Runtime/W9ParticleShaderFlags.cs`

### `NBShaders2`

Characteristics:

- shader remains feature-heavy, but editor architecture is being decomposed
- GUI is composed from item classes and big blocks
- shared editor infrastructure is pulled from `XuanXuanRenderUtility`
- migration appears feature-by-feature rather than through a full rewrite at once

Typical reference files:

- `NBShaders2/Shader/NBShader.shader`
- `NBShaders2/Editor/NBShaderGUI.cs`
- `NBShaders2/Editor/ShaderGUIItems/ModeBigBlockItem.cs`
- `NBShaders2/Editor/ShaderGUIItems/BaseOptionBigBlockItem.cs`

## Important Observations for Cross-Version Work

- `NBShaders2/Shader/NBShader.shader` still carries a large amount of property compatibility from `NBShaders`
- the shader is currently hidden from normal menus, which suggests it is still under active internal iteration rather than ready for broad production use
- editor modularization has started, but only part of the old inspector surface has been rebuilt
- the old flag system remains important because new UI blocks still rely on `W9ParticleShaderFlags`
- many production fixes may still belong directly in `NBShaders` rather than being redirected into `NBShaders2`

## Suggested Working Strategy

First decide which track the task belongs to:

- `NBShaders` maintenance or production bugfix
- `NBShaders2` feature development or migration

### For `NBShaders` maintenance

1. Fix the production path directly in `NBShaders`.
2. Trace the full chain:
   - shader properties
   - HLSL or ShaderLab behavior
   - editor-side mutations
   - packed flag usage
3. Keep the fix minimal and preserve existing serialized behavior unless change is intentional.

### For `NBShaders2` development

When adding or migrating a feature:

1. Find the behavior in `NBShaders` first.
2. Locate the corresponding shader property, packed flag, keyword, and inspector logic.
3. Decide whether `NBShaders2` should preserve the same serialized property contract or intentionally diverge.
4. Port the feature in small vertical slices:
   - shader properties
   - HLSL or ShaderLab behavior
   - inspector block
   - material keyword or render-state synchronization
5. Verify that the new modular GUI still drives the real material state correctly.

## Common Pitfalls

- Porting a property without porting the related inspector state logic
- Porting inspector UI without updating render queue, blend, z-write, or keyword side effects
- Treating `NBShaders2` as fully independent when it still relies on legacy flags and naming
- Changing packed flag meaning without checking old materials or helper code
- Editing only shader code when the behavior is also controlled from editor-side material mutations

## Fast Entry Points

Use these files when starting investigation:

- legacy shader baseline: `GitRepo/NB_FX/NBShaders/Shader/ParticleBase.shader`
- legacy inspector baseline: `GitRepo/NB_FX/NBShaders/Editor/ParticleBaseGUI.cs`
- legacy flag definitions: `GitRepo/NB_FX/NBShaders/Runtime/W9ParticleShaderFlags.cs`
- new shader entry: `GitRepo/NB_FX/NBShaders2/Shader/NBShader.shader`
- new inspector entry: `GitRepo/NB_FX/NBShaders2/Editor/NBShaderGUI.cs`
- new modular GUI root: `GitRepo/NB_FX/XuanXuanRenderUtility/Editor/ShaderGUIItems/ShaderGUIRootItem.cs`
