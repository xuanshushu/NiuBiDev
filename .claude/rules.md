# Global Rules

These rules define strict behavior constraints when working on this project.
They MUST be followed at all times.

---

# Code Modification Rules

* ALWAYS modify existing code instead of creating new systems
* Prefer the smallest possible change that fixes the issue
* DO NOT refactor large systems unless explicitly requested
* DO NOT introduce breaking changes
* DO NOT rename public APIs without strong justification
* DO NOT move files or change structure unless necessary

---

# Scope Control (CRITICAL)

* Primary working directory is: `GitRepo/NB_FX`
* DO NOT modify files outside this directory unless explicitly required
* DO NOT modify `Assets/`, `ProjectSettings/`, or other packages unless the task requires it
* If modification outside scope is needed, EXPLAIN why first

---

# Safety Rules

* NEVER guess implementation details
* ALWAYS search the codebase before making changes
* If unsure, gather more context instead of assuming
* DO NOT invent Unity, URP, or Shader APIs
* DO NOT write code that "might work" — ensure correctness

---

# Shader-Specific Rules (CRITICAL)

## Keywords

* DO NOT introduce new keywords unless absolutely necessary
* ALWAYS consider shader variant explosion before adding keywords
* Prefer `shader_feature` over `multi_compile`
* Reuse existing keywords whenever possible

---

## Pass & LightMode

* DO NOT modify or remove existing passes without understanding full impact
* Ensure all passes have correct LightMode tags
* DO NOT add redundant passes

---

## SRP / URP

* DO NOT break SRP Batcher compatibility
* DO NOT introduce per-material data that disables batching
* Ensure compatibility with URP pipeline

---

## HLSL Logic

* DO NOT duplicate logic across passes
* Prefer modifying existing functions
* Keep math consistent with existing coordinate space usage

---

## ShaderFlag Protocol (CRITICAL)

* DO NOT bypass the existing packed ShaderFlag mechanism with ad-hoc bool state or scattered replacement logic
* DO NOT change the meaning, position, or storage index of existing flag bits unless the migration impact is fully understood and intentionally handled
* Treat C# flag definitions, shader property names, and HLSL flag macros as one protocol that must stay synchronized
* When changing flag-driven behavior, verify the full chain:
  * runtime flag writes
  * material properties
  * inspector logic
  * HLSL reads
  * default shader values
* DO NOT break foldout-state flags, UV mode bit packing, wrap-mode bit packing, color-channel packing, custom-data packing, or p-noise blend packing by making partial edits
* Preserve compatibility for existing materials unless an explicit migration is part of the task

---

# Performance Rules

* DO NOT introduce unnecessary texture sampling
* DO NOT add dynamic branching in fragment shader unless justified
* DO NOT increase shader complexity without reason
* ALWAYS consider runtime cost of changes

---

# Editor (IMGUI) Rules

* DO NOT replace IMGUI with UI Toolkit
* Keep inspector logic simple and readable
* Ensure UI reflects actual shader state (keywords / properties)
* DO NOT introduce unnecessary allocations

---

# Compatibility Rules

* MUST support Unity 2021.3 → latest
* DO NOT use APIs unavailable in older Unity versions unless guarded
* Use preprocessor directives when necessary
* Ensure ShaderLab and HLSL compatibility across versions

---

# Problem Solving Rules

When solving any issue:

1. ALWAYS search for relevant code first
2. Identify exact problem type:
   * ShaderLab
   * HLSL
   * Keyword
   * URP pipeline
3. Understand existing implementation
4. Identify root cause (NOT symptoms)
5. Apply minimal fix
6. Validate side effects

---

# What You Must NOT Do

* DO NOT rewrite systems unless explicitly asked
* DO NOT introduce new architecture without justification
* DO NOT over-engineer solutions
* DO NOT increase shader variant count blindly
* DO NOT ignore performance implications
* DO NOT ignore backward compatibility

---

# Preferred Working Style

* Think like a senior Unity graphics engineer
* Be precise and conservative
* Favor stability over cleverness
* Read more code before acting
* Make changes that are easy to review and revert

---
