# Icon Controller

Windows 文件图标与默认应用配置工具。通过图形界面选择后缀、打开程序和图标，生成可审阅、可备份、可还原的 PowerShell 脚本。

## 快速开始

双击根目录的 **`IconController.cmd`**。首次启动时自动编译，之后启动 `build/IconController.exe`。

1. 新建后缀，或选择左侧已有配置。
2. 选择默认打开程序的 EXE。
3. 从「软件自带图标库」选择 Julia / Typst，或浏览 ICO、EXE、DLL 图标文件。
4. 点击「生成应用脚本」，在打开的目录中双击 `apply.cmd`。
5. 回到应用查看执行结果；需要还原时运行同一版本的 `restore.cmd`。

应用本身管理配置和生成脚本，不直接更改文件关联。脚本执行成功与资源管理器的视觉效果分开判断。

## 功能

- 后缀搜索、程序选择、图标预览、EXE/DLL 资源索引。
- 内置 Julia、Typst 图标，编译时嵌入 EXE，不依赖开发机器路径。
- 本地配置仓库，支持编辑、移除、JSON 导入和导出。
- 独立脚本版本、执行回执、修改前备份和还原入口。
- 脚本拒绝在已知代理隔离环境中修改注册表，避免出现“命令成功、桌面无变化”。

## 项目结构

```text
Ico/
├── IconController.cmd       # 启动入口
├── IconController.csproj    # Visual Studio / MSBuild 项目
├── src/                     # WinForms 界面、仓库与脚本模板
├── tests/                   # 独立测试程序
├── scripts/                 # 构建与测试命令
├── assets/
│   ├── catalog.json         # 内置图标目录
│   ├── julia/               # Julia ICO 与 SVG 源素材
│   └── typst/               # Typst ICO 与 PNG 源素材
├── docs/                    # 数据结构、迁移和同类项目
├── build/                   # 本地编译产物（Git 忽略）
└── data/                    # 个人仓库、脚本与备份（Git 忽略）
```

本机旧的 `julia/`、`typst/` 仅为指向 `assets/` 对应目录的兼容链接，未纳入 Git，用于继续支持已写入 Windows 注册表的旧图标路径。

## 构建与测试

环境：Windows，.NET Framework 4.8 和 Windows PowerShell 5.1。日常使用不需要 .NET SDK、Python、Node.js 或 NuGet 依赖。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1
```

测试程序和应用分开构建。成功后自动清理测试夹具，报告保留在 `build/test-results.txt`。测试不会修改真实的文件关联。Visual Studio 构建需要 .NET Framework 4.8 开发工具包；自带构建脚本使用 Windows 的 Framework 编译器。

## 数据与还原

个人配置位于 `data/library.json`，图标副本位于 `data/assets/`，生成版本位于 `data/scripts/`。这些文件包含本机程序路径及备份，不提交到 Git。迁移前的有效备份保存在 `data/legacy-backups/`。

生成脚本时将 ICO 打包，执行时复制到当前用户的 `%LOCALAPPDATA%/IconController/Icons/`；删除脚本包不会影响这些已应用图标。EXE / DLL 图标依赖所选资源文件继续存在。

「移出仓库」只删除管理记录。还原脚本会检查配置是否已被后来操作替换；恢复旧用户选择时写回先前生效的文件类型，不伪造 Windows 关联哈希。详见 [架构与迁移](docs/architecture.md)。

## 同类项目

- [CustomFileIcons](https://github.com/maxkagamine/CustomFileIcons)：JSON 配置驱动的自定义图标与打开程序管理，概念最接近；仓库已归档。
- [Pineapple Association Manager](https://github.com/BLumia/pineapple-assoc-manager)：面向便携应用，通过配置文件和界面管理关联。
- [Windows File Association and Icon Manager](https://github.com/zoxknez/windows-File-Association-Icon-Manager)：PowerShell 图标覆盖、查询与恢复工具。

检索日期：2026-09-22。完整比较见 [同类项目调研](docs/related-projects.md)。未引入上述项目的代码。

## 素材

Julia、Typst 素材沿用本目录已有文件；来源说明见 [assets/README.md](assets/README.md)。代码与第三方标识的授权应分别处理，本项目暂未指定公开发布许可证。
