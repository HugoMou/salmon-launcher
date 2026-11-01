# Salmon Launcher (SL)

[简体中文](README.md) | **繁體中文** | [English](README.en.md) | [日本語](README.ja.md)

一個以 **WinUI 3 / Windows App SDK** 與 **.NET 10** 打造的 Minecraft Java 版啟動器。安裝、啟動、實例與模組、連線、皮膚預覽、崩潰診斷與主題切換全部整合在一個雙欄介面裡。

- 版本：1.0.0
- 平台：Windows 10 (2004+) / Windows 11，x64
- 授權：GNU General Public License v3.0 或更新版本（GPL-3.0-or-later）

---

## 功能

| 模組 | 能力 |
| --- | --- |
| 啟動管理 | 實例選擇、記憶體與視窗大小、附加 JVM 參數、一鍵啟動與執行狀態提示 |
| 下載安裝 | Mojang 官方版本清單、載入器（Fabric / Quilt / Forge / NeoForge）、多來源回退、中斷續傳、SHA-1 校驗 |
| 實例與模組 | 實例獨立目錄、模組啟停（`.jar` ↔ `.jar.disabled`）、匯入本機模組、存檔備份 |
| 連線 | 自建中繼、區域網路掃描、手動中繼三種接入方式 |
| 內容倉庫 | Modrinth 檢索：模組 / 光影 / 資源包，依實例安裝到對應目錄 |
| 外觀 | 四種語言、深淺主題、自訂強調色、方塊取色主題工坊、壁紙 |
| 診斷 | 讀取 `logs/latest.log` 尾部、崩潰特徵比對與處置建議 |
| 帳戶 | 微軟正版（內建瀏覽器 / 裝置碼 / 系統瀏覽器三種登入）與無限量離線帳戶 |

## 系統需求

- Windows 10 版本 2004（組建 19041）或更新，或 Windows 11；僅支援 x64。
- 首次啟動需連網取得版本清單；遊戲本體、執行庫與資源檔案皆從 Mojang 官方來源或其公開鏡像下載。
- Java 執行環境預設按需自動取得（Eclipse Temurin），也可使用本機已安裝的 Java。

## 下載與安裝

**最新版本：v1.0.0**

| 形式 | 檔案 | 說明 |
| --- | --- | --- |
| 安裝包（推薦） | `SL-Setup-1.0.0-win64.exe` | 標準 Windows 安裝精靈，自動建立開始功能表捷徑與卸載項目；可選擇僅為目前使用者安裝（無需系統管理員權限） |
| 免安裝版 | `SL-1.0.0-portable-win64.zip` | 解壓即用，不寫入登錄檔；適合放在隨身碟 |

兩者都包含完整的 .NET 10 與 Windows App SDK 執行階段，**無需另外安裝任何執行階段**。下載位址：[GitHub Releases](https://github.com/HugoMou/salmon-launcher/releases)。

安裝包為未簽章版本，首次執行時 Windows SmartScreen 可能提示「更多資訊 → 仍要執行」。卸載程式會保留 `%APPDATA%\SalmonLauncher` 下的實例與存檔。

## 建置

需要 .NET 10 SDK 與 Windows App SDK 1.8（透過 NuGet 自動還原）。在倉庫根目錄執行：

```powershell
& "C:\Program Files\dotnet\dotnet.exe" build .\SalmonLauncher.sln -c Debug -p:Platform=x64
```

產物位於：

```
src\SL.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\SL.exe
```

發佈建置可加上 `-c Release`。專案啟用了 `TreatWarningsAsErrors`，任何警告都會中斷建置。

### 產生安裝包與免安裝版

```powershell
.\installer\build-installer.ps1 -Version 1.0.0
```

腳本會先執行 `dotnet publish`（Release、自包含），再用 Inno Setup 編譯 `installer\SL.iss`，最後在 `dist\` 產生 `SL-Setup-<版本>-win64.exe` 與 `SL-<版本>-portable-win64.zip`。需先安裝 [Inno Setup 6](https://jrsoftware.org/isinfo.php)。

## 專案結構

```
src\SL.App    表示層（WinUI 3 頁面、控制項、導覽、在地化、主題）
src\SL.Core   業務層（下載引擎、啟動流程、中繼資料、帳戶、診斷、連線）
src\SL.Data   資料層（目錄配置、原子寫入、設定與快取）
```

三層嚴格單向引用：`SL.App → SL.Core → SL.Data`。

## 資料目錄

所有設定、帳戶、日誌、實例與存檔都保存在本機：

```
%APPDATA%\SalmonLauncher
```

更換電腦時複製整個目錄即可移轉；解除安裝本程式不會自動刪除其中的實例與存檔。

## 下載來源

- 官方來源：`piston-meta.mojang.com`、`libraries.minecraft.net`、`resources.download.minecraft.net`
- 國內鏡像：`bmclapi2.bangbang93.com`（可在設定中切換優先鏡像）
- Java：`api.adoptium.net`、TUNA 鏡像、GitHub Releases
- Modrinth：官方 API 與國內鏡像（可切換）

所有下載檔案最後都會做 SHA-1 或 SHA-256 校驗，校驗失敗會重新下載。

## 多語言

介面提供 **简体中文**、**繁體中文**、**English**、**日本語** 四種語言，預設跟隨系統，可在「外觀與設定」中隨時切換。授權與條款文字同樣提供四種語言版本。

## 版權與授權

```
Salmon Launcher (SL)
Copyright (C) 2026 Hugo Mou

本專案以 GNU General Public License 第 3 版（或任何更新版本）發佈。
完整授權條文見倉庫根目錄的 LICENSE 檔案，或造訪
https://www.gnu.org/licenses/gpl-3.0.html
```

你可以在 GPL 條款下自由執行、研究、修改與再發佈本軟體（包含收費發佈），但必須保留版權與授權聲明，並向接收者提供完整對應的原始碼。

## 免責聲明

- 本軟體為第三方免費工具，與 **Mojang Studios**、**Microsoft** 無任何隸屬或授權關係；**Minecraft** 是 Mojang Studios 的商標。
- 本軟體不內建、不分發、不修改任何 Minecraft 遊戲檔案，遊戲檔案皆從官方來源或其公開鏡像取得。
- 模組、光影與資源包來自 Modrinth 等公開平台，版權歸各自作者所有。
- 軟體按「現狀」提供，不附帶任何明示或暗示的擔保；請在重要操作前自行備份存檔與實例。
- 使用本軟體即表示你已閱讀並同意軟體內的《使用者條款與免責聲明》。
