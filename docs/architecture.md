# 架构与迁移

## 组成

- `src/App.cs`：Windows Forms UI。程序从 `build/` 启动时使用项目根目录的 `data/`；单独分发 EXE 时使用 EXE 同级的 `data/`。
- `src/Core.cs`：数据模型、原子化 JSON 保存、素材导入、脚本生成和回执匹配。
- `src/ApplyTemplate.ps1`：配置通过 Base64 JSON 作为数据嵌入；生成脚本执行前备份受影响的注册表项，提供还原流程。
- `assets/catalog.json` 与两份 ICO：编译为嵌入资源，首次使用时释放为个人仓库中的内容哈希文件。内置素材与已调整后缀记录分开管理，新安装不假定任何文件关联已生效。
- `tests/`：独立控制台测试程序，不随 GUI 编译。

应用使用后缀专属的 `IconController.<extension>` 文件类型配置，避免修改共享应用的 DefaultIcon。哈希保护的 UserChoice / UserChoiceLatest 不直接伪造；有冲突时尝试清除，并在权限失败时报告与回滚。

## 目录迁移（2026-09-22）

此前位于 `IconController/` 的源码拆分为 `src/`、`tests/`、`scripts/`，有效个人数据迁至 `data/`。配置图标地址、历史目录、回执及状态文件中的备份目录已更新；对应配置指纹随迁移更新，保留原有执行状态。

原始 Julia、Typst 素材实际移至 `assets/julia/`、`assets/typst/`。本机在旧位置保留目录兼容链接，避免先前注册的绝对路径失效。兼容链接不进入 Git。

保留有效桌面备份、已执行脚本及迁移前配置；删除调试采样、Process Monitor 下载与日志、缓存备份、废弃 IconHandler、临时测试目录及早期试验脚本。

## 已确认的隔离行为

早期图标设置失败来自注册表视图隔离：Process Monitor 同时记录到命令进程访问 `\\REGISTRY\\WC\\Silo...`，而桌面 Explorer 使用普通用户注册表。即使同一用户 SID，甚至代理请求提权，两者也可能看到不同内容。桌面手动运行修复脚本后用户已确认成功。

因此实际脚本会检查代理环境标记并拒绝在那里执行。`-ValidateOnly` 只检查配置和文件，不改注册表，也不写入“已执行”回执。原始诊断日志包含大量本机活动，整理时已删除，仅保留这份结论。
