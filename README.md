# Icon Controller

Windows 文件图标与默认应用配置工具。选择后缀、打开程序和图标，生成带备份及还原功能的 PowerShell 脚本。内置 Julia、Typst 图标，支持配置导入、导出和历史管理。

## 使用

双击根目录的 **`IconController.exe`** 启动，无需命令窗口。若从 Git 获取源码、目录里没有 EXE，请先运行下方的构建命令。

1. 新建或选择后缀，填写默认应用 EXE，并选择内置图标或本地 ICO / EXE / DLL。
2. 点击「保存配置」，或点击「生成应用脚本」保存并生成当前项。
3. 点击「一键生成全部配置」，生成仓库全部已保存配置的统一脚本；当前编辑有改动时会先保存。
4. 在固定目录 `data/scripts/` 双击 `apply-all.cmd` 应用全部配置，或 `apply.cmd` 应用最近生成的单项。
5. 使用对应的 `restore-all.cmd` / `restore.cmd` 还原；旧版本入口在 `history/`，也可从软件的脚本历史打开。

每次生成更新固定入口，并保留独立历史版本。全部生成会先检查所有配置；执行阶段逐项处理，遇到失败停止，先前成功项保留，可用本批次的还原入口撤销。还原按反序处理，并拒绝覆盖后来发生的关联更改。

ICO 数据直接嵌入 `.ps1`，无需逐后缀分发脚本或另带图标。执行后图标复制到 `%LOCALAPPDATA%/IconController/Icons/`。所选默认程序与 EXE / DLL 图标资源仍须存在。复制脚本到其他目录执行时，回执和备份保存在新位置，原仓库不会自动读取它们。

## 文件布局

```text
IconController.exe       应用及启动入口（Git 忽略）
IconController.csproj    可选的 Visual Studio / MSBuild 项目
src/                     界面、配置管理和脚本模板
assets/                  内置图标目录及源素材
scripts/                 构建与旧路径清理工具
data/                    个人配置、脚本及备份（Git 忽略）
  library.json           后缀配置与历史索引
  assets/                导入图标的副本
  scripts/               固定的单项 / 全部执行入口
    history/             历史脚本、逐项回执和还原备份
  legacy-backups/        迁移前配置与旧版有效备份
```

应用始终使用 EXE 同级的 `data/`，与启动时的工作目录无关。移动应用时请一并保留 `data/`；已有配置中的外部绝对路径仍须有效。移出仓库只移除管理记录，保留已生成文件和系统设置。执行状态来自匹配该版本的回执，生成脚本不等于已经应用。

根目录的 `julia/`、`typst/` 是本机旧图标路径的兼容链接，实际素材位于 `assets/`。需要移除链接时，从桌面运行 `data/scripts/remove-legacy-links.cmd`；工具先备份并迁移旧图标引用，验证后仅删除链接，不删除素材。

## 构建

需要 Windows、.NET Framework 4.8 和 Windows PowerShell 5.1，无需 Node.js、Python 或 NuGet。源码修改后运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

构建脚本使用系统自带 Framework 编译器，将 EXE 直接输出到项目根目录，并嵌入 `assets/app/icon-controller.ico`。SVG 源文件和 PNG 预览也保存在 `assets/app/`。通过 Visual Studio / MSBuild 构建则需要 .NET Framework 4.8 开发工具包。项目不保留独立测试工程、测试脚本或测试产物。

## 文件关联与备份

每个后缀使用独立的 `IconController.<后缀>` 文件类型，避免共享应用的图标互相覆盖。脚本在修改前备份；发生冲突时尝试清除 UserChoice / UserChoiceLatest，并在权限失败时报告及回滚。还原旧用户选择时恢复其生效的文件类型，不伪造 Windows 关联哈希。

脚本应从 Windows 桌面执行。此前已确认代理命令与资源管理器可能使用不同的注册表视图，因此脚本拒绝在已知代理隔离环境中修改关联。`-ValidateOnly` 仅检查配置，不写注册表、执行回执或备份；最终图标显示以资源管理器为准。

## 素材

Julia、Typst 素材沿用项目已有文件，来源见 [assets/README.md](assets/README.md)。代码与第三方标识的授权分别处理，本项目暂未指定公开发布许可证。

## GitHub 同类项目

检索与 README 核对日期：2026-09-22。以下是项目公开声明的功能；未下载或运行它们，Windows 11 当前版本兼容性未经本机验证。

| 项目 | 相似能力 | 与 Icon Controller 的差异 |
| --- | --- | --- |
| [maxkagamine/CustomFileIcons](https://github.com/maxkagamine/CustomFileIcons) | 用 JSON 为文件后缀配置程序、图标及额外菜单，C# 实现 | 概念最接近；通过编辑 JSON 和 apply.bat 应用，注册为默认应用；仓库于 2021-11-03 归档。当前工具提供表单、内置素材库、独立脚本版本及回执。 |
| [BLumia/pineapple-assoc-manager](https://github.com/BLumia/pineapple-assoc-manager) | 便携应用关联管理、图形界面、自定义命令和图标、默认应用注册 | 使用 INI 风格的 .pacfg 配置，C++ / Qt 6，侧重随便携软件分发关联管理器；当前工具侧重个人管理多个后缀及生成可独立执行的脚本。 |
| [zoxknez/windows-File-Association-Icon-Manager](https://github.com/zoxknez/windows-File-Association-Icon-Manager/blob/main/README.en.md) | PowerShell 设置图标、查询当前 ProgID、保存旧值并恢复 | 侧重修改当前关联的图标而不更改默认应用。README 列出的探测链为 UserChoice → HKCR；未据此确认其支持 UserChoiceLatest。当前工具同时选择应用与图标，并保留脚本历史。 |

这些项目说明需求已有成熟方向可参考。本次仅作功能调研，未复制源码，也未将兼容性声明当作本机验证结果。
