# 原始参考项目与许可证

轻启是独立实现，不是第三方项目的直接 fork，也不代表其作者或官方发行版。

## 参考实现与设计

- **Faustvii/StartupManager** — https://github.com/Faustvii/StartupManager ，MIT。参考自启动来源分类、注册表读取思路。曾核对 `src/StartupManager.Services/Registries/RegistryService.cs`，blob `a0f4f9f2b6efca9bf1262feeaacdb1177e81de85`。没有纳入修改第三方启动项的功能。[完整许可证](references/StartupManager-MIT.txt)。2026-11-21 核对上游 `LICENSE.md`，blob `d775f588ba06dbc654ed1eb58ff2c5fe255b184e`。
- **AlexanderPro/SmartSystemMenu** — https://github.com/AlexanderPro/SmartSystemMenu ，MIT。参考 Win32 窗口枚举、显示状态控制和优先级设计。没有纳入全局 Hook、注入模块或原发行包。[完整许可证](references/SmartSystemMenu-MIT.txt)。2026-11-21 核对上游 `LICENSE`，blob `e0683d2cafe0e132eafeaad4e8037fdc229d22bf`。

原许可证文本保留上游署名；StartupManager 上游许可证的 copyright 行未写明作者，随附文本保持原样。

## 仅用于功能比较

- [jeffcox111/LaunchLater](https://github.com/jeffcox111/LaunchLater)：GPL-2.0；比较图形化延迟启动，没有复制或分发其代码。
- [benbuck/rbtray](https://github.com/benbuck/rbtray)：GPL；比较托盘恢复交互，没有复制或分发其代码或 Hook。
- [syx594/DelayedStartupToolPro](https://github.com/syx594/DelayedStartupToolPro)：调研时未找到明确许可证，仅比较功能，没有复制代码或分发其程序。

本项目主程序与助手图标由 `Make-Icons.ps1` 绘制。界面中的目标软件图标在用户本机按软件路径提取，不随公开发行包分发第三方软件图标或程序。
