# 全体設計

YMM4 クレジット自動出力プラグインの構成と、主要な処理の仕組み。利用者向けの説明は `README.md` を参照。

## 構成

```
src/YMM4CreditPlugin.Core/   # YMM4 に依存しない純粋な C#(net10.0)。テスト対象
src/YMM4CreditPlugin/        # YMM4 プラグイン本体(WPF、net10.0-windows)
tests/YMM4CreditPlugin.Core.Tests/
packaging/Readme.txt         # .ymme に同梱する Readme のテンプレート({VERSION} を置換)
scripts/release.ps1          # 配布物の作成と GitHub Release
```

ロジックは Core に置き、プラグイン側は画面と YMM4 との橋渡しだけにする。Core は YMM4 の DLL なしでビルド・テストできる。

## 処理の流れ

1. `YmmpParser.Parse` が ymmp(JSON)を読み、`UsedMaterial` の一覧を返す
   - タイムラインの全要素を再帰的にたどり、絶対パスの文字列(`FilePath` プロパティ)をファイル素材、`Font` をフォントとして拾う
   - キャラ設定(`Characters`)の `TachieCharacterParameter.Directory` 配下のファイルは、個別のファイルにせずキャラの立ち絵 1 件にまとめる。立ち絵フォルダの場所はユーザーごとに違うため、フォルダ名で決め打ちしない
   - 声・立ち絵は、タイムラインから参照されたキャラ(`CharacterName`)の分だけ加える
2. `CreditDatabase`(登録データ)と照合し、`CreditBuilder.Build` が `クレジット.txt` の本文を組み立てる
   - 見出しの順は `MaterialKind` の宣言順。改行は CRLF
   - 出典の `Merge` が on なら、その出典の素材は種類をまたいでもまとめ行を 1 回だけ出す(出力先は `MergedKind`、未指定なら出力順で先頭の種類)
   - 未登録・クレジットが空の素材は、末尾の「クレジット該当なし」に並べる

## 素材の照合キー

素材は `(MaterialKeyType, Key)` で照合する。

| 種類 | キー |
|------|------|
| ファイル | 絶対パス |
| 立ち絵 | キャラ素材フォルダの絶対パス |
| 声 | `"API|Arg"` |
| フォント | フォント名 |

比較は `MaterialKey.Comparer`(大文字小文字を区別しない)にそろえる。素材ファイルを移動・改名すると別の素材になる。

## 登録データ

- `credit-data.json` はプラグイン DLL と同じフォルダに保存する(`CreditToolPlugin.DataFilePath`)。リポジトリには含めない
- `CreditDatabaseStore.Save` は一時ファイルに書いてから置き換える。読み込みに失敗したときは、元のファイルを壊さないよう画面での変更を保存しない
- 画面の項目が変わるたびに `CreditToolViewModel.Save` が `SyncSources` を呼び、出典一覧を素材の出典に合わせる(使われなくなった出典は消える)

## YMM4 との連携

- プラグインの入口は `CreditToolPlugin`(`IToolPlugin`)。同時に 1 画面だけ開ける(複数開くと互いの登録データを上書きするため)
- 開いているプロジェクトのパスと保存状態は、公式 API が無いため `CurrentProjectLocator` がメインウィンドウの ViewModel をリフレクションで読む。YMM4 の更新で壊れうるので、取得できないときは ymmp を選ぶ操作に誘導する
- ビルドは YMM4 本体の `YukkuriMovieMaker.Plugin.dll` を参照する(環境変数 `YMM4_PATH`)。Release ビルドのときだけプラグインフォルダへ配置する(テストのビルドで、YMM4 起動中の DLL を上書きしようとしないため)

## 配布

- 配布物には `YMM4CreditPlugin.dll` と `YMM4CreditPlugin.Core.dll` だけを入れる(YMM4 同梱の DLL は入れない)
- `.ymme` は zip の拡張子を変えたもの。zip の一番上のフォルダ名が、インストール先のプラグインフォルダ名になる。同梱の `Readme.txt` はインストーラーに表示される
- インストーラーは既存のファイルを上書きし、`credit-data.json` は残す
