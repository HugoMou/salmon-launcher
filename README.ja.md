# Salmon Launcher (SL)

[简体中文](README.zh-CN.md) | [繁體中文](README.zh-Hant.md) | [English](README.md) | **日本語**

**WinUI 3 / Windows App SDK** と **.NET 10** で作られた Minecraft Java Edition ランチャーです。インストール、起動、バージョン管理と Mod、マルチプレイ、スキン表示、クラッシュ診断、テーマ切り替えを 1 つの 2 ペイン画面にまとめています。

- バージョン: 1.0.2
- 対応 OS: Windows 10 (2004 以降) / Windows 11、x64 のみ
- ライセンス: GNU General Public License v3.0 以降(GPL-3.0-or-later)

---

## 機能

| 分野 | できること |
| --- | --- |
| 起動 | バージョン選択、メモリとウィンドウサイズ、追加 JVM 引数、ワンクリック起動と実行状態の表示 |
| インストール | Mojang 公式バージョン一覧、ローダー(Fabric / Quilt / Forge / NeoForge)、ミラーへの自動フォールバック、レジューム、SHA-1 検証 |
| バージョンと Mod | バージョンごとの独立フォルダー、Mod の有効/無効(`.jar` ↔ `.jar.disabled`)、ローカル Mod の取り込み、セーブのバックアップ |
| マルチプレイ | 自前リレー、LAN スキャン、手動リレー |
| コンテンツ | Modrinth で Mod・シェーダー・リソースパックを検索し、選択中のバージョンへ導入 |
| 外観 | 4 言語、ライト/ダークテーマ、アクセントカラーの変更、ブロック配色のテーマ工房、壁紙 |
| 診断 | `logs/latest.log` の末尾を読み、クラッシュの特徴を照合して対処案を提示 |
| アカウント | Microsoft サインイン(内蔵ブラウザー / デバイスコード / システムブラウザー)と無制限のオフラインアカウント |

## 動作環境

- Windows 10 バージョン 2004(ビルド 19041)以降、または Windows 11。x64 のみ対応。
- 初回起動時にバージョン一覧を取得するためネット接続が必要です。ゲーム本体・ライブラリ・アセットは Mojang 公式ソースまたは公開ミラーから取得します。
- Java は既定で必要時に自動取得します(Eclipse Temurin)。既存の Java を使うこともできます。

## ダウンロードとインストール

**最新リリース: v1.0.2**

| 形式 | ファイル | 説明 |
| --- | --- | --- |
| インストーラー(推奨) | `SL-Setup-1.0.2-win64.exe` | **简体中文 / 繁體中文 / English / 日本語** の 4 言語 UI を備えた標準の Windows セットアップウィザード。スタートメニューのショートカットとアンインストール項目を作成し、管理者権限なしで現在のユーザーにのみインストールすることもできます |
| ポータブル版 | `SL-1.0.2-portable-win64.zip` | 解凍してすぐ使えます。レジストリに書き込まないため、USB メモリなどに入れて持ち運べます |

どちらも .NET 10 と Windows App SDK のランタイムを同梱しており、**別途ランタイムをインストールする必要はありません**。ダウンロード: [GitHub Releases](https://github.com/HugoMou/salmon-launcher/releases)。

インストーラーは未署名のため、初回起動時に Windows SmartScreen が「詳細情報 → 実行」を求める場合があります。アンインストールしても `%APPDATA%\SalmonLauncher` のインスタンスとセーブデータは保持されます。

## ビルド

.NET 10 SDK と Windows App SDK 1.8 が必要です(NuGet で自動復元)。リポジトリ直下で実行します。

```powershell
& "C:\Program Files\dotnet\dotnet.exe" build .\SalmonLauncher.sln -c Debug -p:Platform=x64
```

出力先:

```
src\SL.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\SL.exe
```

リリースビルドは `-c Release` を付けます。`TreatWarningsAsErrors` が有効なため、警告があるとビルドは失敗します。

### インストーラーとポータブル版の作成

```powershell
.\installer\build-installer.ps1 -Version 1.0.2
```

このスクリプトは `dotnet publish`(Release、自己完結)を実行し、Inno Setup で `installer\SL.iss` をコンパイルして、`dist\` に `SL-Setup-<バージョン>-win64.exe` と `SL-<バージョン>-portable-win64.zip` を生成します。[Inno Setup 6](https://jrsoftware.org/isinfo.php) が必要です。

## プロジェクト構成

```
src\SL.App    プレゼンテーション層(WinUI 3 のページ、コントロール、ナビゲーション、ローカライズ、テーマ)
src\SL.Core   ビジネス層(転送エンジン、起動パイプライン、メタデータ、アカウント、診断、マルチプレイ)
src\SL.Data   データ層(フォルダー構成、アトミック書き込み、設定とキャッシュ)
```

依存関係は `SL.App → SL.Core → SL.Data` の一方向のみです。

## データフォルダー

設定、アカウント、ログ、バージョン、ワールドはすべてローカルに保存されます。

```
%APPDATA%\SalmonLauncher
```

フォルダーごとコピーすれば別の PC へ移行できます。アンインストールしてもバージョンやワールドは削除されません。

## ダウンロード元

- 公式: `piston-meta.mojang.com`、`libraries.minecraft.net`、`resources.download.minecraft.net`
- 中国国内ミラー: `bmclapi2.bangbang93.com`(設定でミラー優先を切り替え可能)
- Java: `api.adoptium.net`、TUNA ミラー、GitHub Releases
- Modrinth: 公式 API と中国国内ミラー(切り替え可能)

取得したファイルは最後に SHA-1 または SHA-256 で検証され、不一致なら再取得します。

## 対応言語

UI は **简体中文**、**繁體中文**、**English**、**日本語** の 4 言語に対応し、既定ではシステム言語に従います。「外観と設定」からいつでも切り替えられます。ライセンスと規約の文章も 4 言語で用意しています。

## 著作権とライセンス

```
Salmon Launcher (SL)
Copyright (C) 2026 Hugo Mou

本プロジェクトは GNU General Public License 第 3 版(またはそれ以降)で
公開されています。ライセンス全文はリポジトリ直下の LICENSE、または
https://www.gnu.org/licenses/gpl-3.0.html を参照してください。
```

GPL に従う限り、実行・研究・改変・再配布(有償配布を含む)が認められますが、著作権表示とライセンス表示を保持し、受け取り手に対応する完全なソースコードを提供する必要があります。

## 免責事項

- 本ソフトウェアは第三者の無料ツールであり、**Mojang Studios** や **Microsoft** との提携・承認関係はありません。**Minecraft** は Mojang Studios の商標です。
- 本ソフトウェアは Minecraft のゲームファイルを同梱・配布・改変しません。ファイルは公式ソースまたは公開ミラーから取得します。
- Mod・シェーダー・リソースパックは Modrinth などの公開プラットフォーム由来で、著作権は各作者に帰属します。
- 本ソフトウェアは「現状のまま」提供され、明示・黙示を問わずいかなる保証も伴いません。重要な操作の前にワールドとバージョンを自分でバックアップしてください。
- 本ソフトウェアを使用すると、アプリ内に表示される「利用規約と免責事項」を読み同意したものとみなされます。
