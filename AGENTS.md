# PICO MCP Extensions

本文件约束在 `com.bytedance.pico.mcp-extensions` 仓库内工作的编码 Agent。
该仓库是 Unity UPM package，不是完整 Unity 工程；目标是通过 Unity MCP 工具帮助
Agent 以可重复、可验证的方式构建 PICO XR 功能。

## 当前开发线

- `main` 是稳定发布来源，不直接承载日常功能开发。
- `release/v0.1.0` 是当前开发与集成分支。
- 新功能、修复和优化必须从 `release/v0.1.0` 拉出独立分支，并通过 MR 合回
  `release/v0.1.0`；不要直接提交到 `main` 或开发分支。
- 当前开发版本使用 `0.1.0-alpha.x`。每次功能修改都要递增 `x`，并同时更新
  `package.json` 与 `README.md`，两处版本必须完全一致。
- 稳定版只能从 `main` 生成。发布时显式传入稳定版本号，并使用仓库自带的
  `.scripts/release_local.sh` 或对应 Codebase 发布流水线。

## 仓库结构

- `Editor/`：Unity Editor 内的功能实现和工具入口。
  - `PXR_MCP_Common.cs`：XR Origin 生命周期、运行时判断和公共逻辑。
  - `PXR_MCP_Features.cs`：VST、Controller、Locomotion、Spatial Mesh、Plane、
    Hand、Grab 等 building blocks。
  - `PXR_MCP_Haptics.cs`：控制器触觉能力的 Editor 编排。
  - `PXR_MCP_PackageOps.cs`：Unity Package Manager 操作。
  - `Tools/`：带 `[McpTool]` 的 MCP 工具表面与统一结果类型。
- `Runtime/`：需要随 Player 构建的运行时能力，目前包含统一的触觉管理器。
- `Tests/Editor/`、`Tests/Runtime/`：对应的 Unity Test Framework 测试。
- `.codebase/`、`.scripts/`：内部 CI 与发布工具；公开发布时必须从 GitHub
  目标树中剥离。

## 实现原则

- 简单优先，只实现需求明确要求的能力，不增加推测性抽象。
- 保持幂等：相同参数重复执行应安全返回，不生成重复组件或场景对象。
- 保持非破坏性：不得销毁或禁用非 Agent 创建的 XR Origin 或用户对象。
- 所有场景修改必须接入 Unity Undo，并在需要时明确保存场景。
- 工具边界不得向 MCP bridge 抛出异常；统一转换为 `PXR_MCP_Result`。
- `summary` 保持为可直接展示的一句话，结构化详情放入 `data`。
- MCP 工具命名使用 `pico_xr_*`，C# 类型使用 `PXR_MCP_*`，Agent 创建的场景
  对象使用 `[PICO_MCP]` 前缀。
- 不手工修改用户项目的 `Packages/manifest.json`，包操作走
  `pico_xr_package`。

## 运行时与依赖

- 支持 Unity `6000.0+`，当前 PICO Unity Integration SDK 基线为 `6.1.x`。
- PICO Native 与 PICO OpenXR 代码分别受 `ENABLE_PICO_XR_SDK` 和
  `ENABLE_PICO_OPENXR_SDK` 控制；不要把某一运行时专属逻辑泄漏到另一分支。
- Plane Detection 仅支持 PICO Native，必须保留这一差异。
- 不硬编码随包版本变化的 XRI 路径或类型；优先使用 `PackageInfo`、反射和明确
  的可用性检查。

## 工具表面变更

新增、删除或修改 MCP 能力时，至少同步检查：

- `Editor/Tools/PXR_MCP_Tools.cs` 中的工具、action 和参数描述；
- 对应的 Editor/Runtime 实现；
- `README.md` 的 Features 与 MCP Tools 表格；
- `Tests/Editor/` 与 `Tests/Runtime/` 中的相关测试；
- `unity-agentic-tools` 源仓中的 `pico-unity-buildingblocks` 或相关 Skill。

Skill 内容以 `unity-agentic-tools` 为源，不得先在 `spatial-craft` 发布副本中设计
或增加源仓不存在的行为。源仓验证后，再按工作区规则同步发布副本及契约测试。

## Unity 文件规则

- 新增 Unity 资源或源码时必须同时提交对应 `.meta` 文件。
- 删除文件时同步删除 `.meta`；重命名时保留原 GUID。
- 手动验证菜单必须放在完整、独立的 `#if PICO_MCP_SHOW_MENU ... #endif`
  块中。公开发布默认剥离这些代码块，不得在其中嵌套无关业务逻辑。

## 验证与发布

- 提交前运行 `git diff --check`，并确认 `package.json` 与 README 版本一致。
- 功能变更必须运行相关 Unity Editor/Runtime 测试；静态检查成功不能替代设备
  触觉或真实运行时验证。
- 发布前先运行：

  ```bash
  bash .scripts/release_local.sh --doctor --from main --to main
  bash .scripts/release_local.sh <version> --from main --to main
  ```

  第二条默认只演练，不推送。只有明确收到发布指令后才能增加 `--push`。

- 发布结果必须核对 GitHub `main`、目标 tag、`package.json` 与 README 的版本，
  并确认 `.codebase/`、`.scripts/` 未出现在发布头部。

## Commit 与 MR

- 使用 Conventional Commits，提交只包含当前任务所需修改。
- commit message 和 MR message 必须包含：

  `Co-authored-by: [Trae](https://trae.bytedance.com/)`

- MR 必须关联工作项 `7361867353`。
- 合并前必须确认无冲突、自动检查通过，并区分“CI 已通过”和“真实设备已验证”。
