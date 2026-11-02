# Salmon Launcher (SL)

**简体中文** | [繁體中文](README.zh-Hant.md) | [English](README.md) | [日本語](README.ja.md)

一个使用 **WinUI 3 / Windows App SDK** 与 **.NET 10** 构建的 Minecraft Java 版启动器。安装、启动、实例与模组、联机、皮肤预览、崩溃诊断与主题切换全部集成在一个双栏界面里。

- 版本：1.0.2
- 平台：Windows 10 (2004+) / Windows 11，x64
- 许可：GNU General Public License v3.0 或更新版本（GPL-3.0-or-later）

---

## 功能

| 模块 | 能力 |
| --- | --- |
| 启动管理 | 实例选择、内存与窗口尺寸、附加 JVM 参数、一键启动与运行状态提示 |
| 下载安装 | Mojang 官方版本清单、加载器（Fabric / Quilt / Forge / NeoForge）、多源回退、断点续传、SHA-1 校验 |
| 实例与模组 | 实例隔离目录、模组启停（`.jar` ↔ `.jar.disabled`）、导入本地模组、存档备份 |
| 联机 | 自建中继、局域网扫描、手动中继三种接入方式 |
| 内容仓库 | Modrinth 检索：模组 / 光影 / 资源包，按实例安装到对应目录 |
| 外观 | 四种语言、深浅主题、自定义强调色、方块取色主题工坊、壁纸 |
| 诊断 | 读取 `logs/latest.log` 尾部、崩溃签名匹配与中文处置建议 |
| 账户 | 微软正版（内置浏览器 / 设备码 / 系统浏览器三种登录）与无限量离线账户 |

## 系统要求

- Windows 10 版本 2004（内部版本 19041）或更高，或 Windows 11；仅支持 x64。
- 首次启动需联网获取版本清单；游戏本体、运行库与资源文件均从 Mojang 官方源或其公开镜像下载。
- Java 运行时默认按需自动获取（Eclipse Temurin），也可使用本机已安装的 Java。

## 下载与安装

**最新版本：v1.0.2**

| 形式 | 文件 | 说明 |
| --- | --- | --- |
| 安装包（推荐） | `SL-Setup-1.0.2-win64.exe` | 标准 Windows 安装向导，**提供简体中文 / 繁體中文 / English / 日本語 四种安装界面**，自动创建开始菜单快捷方式与卸载项；可选择仅为当前用户安装（无需管理员权限） |
| 便携版 | `SL-1.0.2-portable-win64.zip` | 解压即用，不写入注册表；适合放在移动硬盘或 U 盘 |

两个文件都包含完整的 .NET 10 与 Windows App SDK 运行时，**无需单独安装任何运行库**。下载地址：[GitHub Releases](https://github.com/HugoMou/salmon-launcher/releases)。

安装包为未签名版本，首次运行时 Windows SmartScreen 可能提示「更多信息 → 仍要运行」。卸载程序会保留 `%APPDATA%\SalmonLauncher` 下的实例与存档。

## 构建

需要 .NET 10 SDK 与 Windows App SDK 1.8（通过 NuGet 自动还原）。在仓库根目录执行：

```powershell
& "C:\Program Files\dotnet\dotnet.exe" build .\SalmonLauncher.sln -c Debug -p:Platform=x64
```

产物位于：

```
src\SL.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\SL.exe
```

发布构建可加 `-c Release`。项目启用了 `TreatWarningsAsErrors`，任何警告都会中断构建。

### 生成安装包与便携版

```powershell
.\installer\build-installer.ps1 -Version 1.0.2
```

脚本会先执行 `dotnet publish`（Release、自包含），再用 Inno Setup 编译 `installer\SL.iss`，最后在 `dist\` 生成 `SL-Setup-<版本>-win64.exe` 与 `SL-<版本>-portable-win64.zip`。需要先安装 [Inno Setup 6](https://jrsoftware.org/isinfo.php)。

## 项目结构

```
src\SL.App    表示层（WinUI 3 页面、控件、导航、本地化、主题）
src\SL.Core   业务层（下载引擎、启动流水线、元数据、账户、诊断、联机）
src\SL.Data   数据层（目录布局、原子写盘、设置与缓存）
```

三层严格单向引用：`SL.App → SL.Core → SL.Data`。

## 数据目录

所有配置、账户、日志、实例与存档都保存在本机：

```
%APPDATA%\SalmonLauncher
```

移动电脑时复制整个目录即可迁移；卸载本程序不会自动删除其中的实例与存档。

## 下载源

- 官方源：`piston-meta.mojang.com`、`libraries.minecraft.net`、`resources.download.minecraft.net`
- 国内镜像：`bmclapi2.bangbang93.com`（可在设置中切换优先镜像）
- Java：`api.adoptium.net`、TUNA 镜像、GitHub Releases
- Modrinth：官方 API 与国内镜像（可切换）

所有下载文件最终都会做 SHA-1 或 SHA-256 校验，校验失败会重新下载。

## 多语言

界面提供 **简体中文**、**繁體中文**、**English**、**日本語** 四种语言，默认跟随系统，可在「外观与设置」中随时切换。许可与条款文本同样提供四种语言版本。

## 版权与许可

```
Salmon Launcher (SL)
Copyright (C) 2026 Hugo Mou

本项目以 GNU General Public License 第 3 版（或任何更新版本）发布。
完整许可证全文见仓库根目录的 LICENSE 文件，或访问
https://www.gnu.org/licenses/gpl-3.0.html
```

你可以在 GPL 条款下自由运行、研究、修改与再分发本软件（包括收费分发），但必须保留版权与许可声明，并向接收者提供完整对应的源代码。

## 免责声明

- 本软件为第三方免费工具，与 **Mojang Studios**、**Microsoft** 无任何隶属或授权关系；**Minecraft** 是 Mojang Studios 的商标。
- 本软件不内置、不分发、不修改任何 Minecraft 游戏文件，游戏文件均从官方源或其公开镜像获取。
- 模组、光影与资源包来自 Modrinth 等公开平台，版权归各自作者所有。
- 软件按「现状」提供，不附带任何明示或暗示的担保；请在重要操作前自行备份存档与实例。
- 使用本软件即表示你已阅读并同意软件内的《用户协议与免责声明》。
