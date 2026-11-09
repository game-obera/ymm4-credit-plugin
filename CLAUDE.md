# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Top-Level Rules

- **思考は英語**で行い、**回答は日本語**で行うこと
- **コードのコメントとログメッセージは日本語**で記述すること
- ライブラリ・.NET / WPF API の使い方は、記憶に頼らず **Context7 MCP** で最新情報を確認すること
- ハードコーディングは避け、調整する値は定数・設定にまとめること
- **公開リポジトリのため、個人環境の絶対パス・機密情報・実際に使っている素材の名前を書かないこと**
  - README・docs・テスト・コミットメッセージのすべてが対象。例やテストデータは架空の名前(`C:\a\bgm.mp3`、`出典1`、`キャラA` など)にする
  - 実在の素材配布サイトを連想させる名前は、変数名にも使わない
  - パスはリポジトリ相対で書く

## Project Overview

ゆっくりMovieMaker4(YMM4)のツールプラグイン。ymmp から使用素材を抽出し、`クレジット.txt` を書き出す。利用者向けの説明は `README.md`、構成と設計は `docs/architecture.md` を参照。

## Commands

ビルドには環境変数 `YMM4_PATH`(YMM4 のフォルダ)が必要。テストは不要。

```bash
# テスト(xUnit)
dotnet test tests/YMM4CreditPlugin.Core.Tests
# 1 件だけ実行(テスト名は日本語のメソッド名)
dotnet test tests/YMM4CreditPlugin.Core.Tests --filter "FullyQualifiedName~SyncSourcesは新しい出典だけ足す"

# Release でビルドすると %YMM4_PATH%\user\plugin\YMM4CreditPlugin\ に DLL を配置する
dotnet build src/YMM4CreditPlugin -c Release
# 配置せずにビルドだけ(YMM4 の起動中は DLL がロックされて配置に失敗する)
dotnet build src/YMM4CreditPlugin -c Release -p:SkipPluginDeploy=true

# 配布物(.ymme / .zip)を dist\ に作る。-DryRun を外すとタグ付けと GitHub Release まで行う
pwsh ./scripts/release.ps1 -Version 1.0.0 -DryRun
```

- 修正を YMM4 で確かめるには `-c Release` でビルドし、YMM4 を再起動する(Debug ビルドは配置されない)

## Workflow Rules

- 作業は `main` から作業ブランチを切って行う
- コミット前に差分を見せ、ユーザーの承認を得てからコミットする
- 画面に関わる変更や新機能は、ユーザー自身が YMM4 上で動かして確認するまでコミットしない
- 構成や処理の仕組みを変えたら `docs/architecture.md` も更新する

### Memory の保存ルール

- git・コード・docs から復元できない情報だけを保存する(設計判断の理由・既知の落とし穴・保留中の判断など)
- ファイル名は `{type}_{トピック}.md`(type は `user` / `feedback` / `project` / `reference`)

## ドキュメントの配置

superpowers の `brainstorming` / `writing-plans` スキルの既定の出力先を、以下で上書きする。

| 種別 | 配置先 |
|------|--------|
| 全体設計 | `docs/architecture.md` |
| 設計 spec | `docs/specs/YYYY-MM-DD-<topic>-design.md` |
| 実行計画(進行中) | `docs/plans/active/YYYY-MM-DD-<feature>.md` |
| 完了した計画 | `docs/plans/completed/YYYY-MM-DD-<feature>.md` |
| 技術的負債 | `docs/plans/debt/<topic>.md` |

実装前に `docs/plans/active/` を確認し、関連する計画があれば参照する。計画が完了したら `completed/` へ移す。
