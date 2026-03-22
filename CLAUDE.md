# NiuBiDev Claude Context

Primary work happens in `GitRepo/NB_FX`.

This package contains the FX rendering stack. Shader work may target either ongoing maintenance in `NBShaders` or new development in `NBShaders2`.

## Default working assumptions

- Prefer changing existing files over creating parallel systems
- Keep fixes minimal, safe, and easy to review
- Search the codebase before answering or editing
- Do not hallucinate Unity, URP, ShaderLab, HLSL, or editor APIs
- Be conservative with shader keywords, pass layout, render queue logic, and SRP Batcher compatibility

## Current shader tracks

- `NBShaders` is the active production shader implementation and may still receive direct maintenance
- `NBShaders2` is the newer modularized shader/editor path under active development
- `XuanXuanRenderUtility` provides shared editor and shader GUI infrastructure used by `NBShaders2`

## Use these files intentionally

- `.claude/rules.md`: hard constraints
- `.claude/project.md`: project knowledge base and architecture notes
- `.claude/skills/`: reusable workflows for shader debugging, keyword audits, and inspector sync
