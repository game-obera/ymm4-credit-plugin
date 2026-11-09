# YMM4 クレジット自動出力プラグイン Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** YMM4 のツールメニューから開くウィンドウで素材ごとのクレジットを登録し、ymmp の使用素材から `クレジット.txt` を出力する。

**Architecture:** ymmp 解析・登録データ・出力組み立ては YMM4 に依存しない `YMM4CreditPlugin.Core`(net10.0)に置き、xUnit でテストする。YMM4 プラグイン本体 `YMM4CreditPlugin`(net10.0-windows / WPF)は `IToolPlugin` と画面、現在のプロジェクト取得(リフレクション)だけを持つ。

**Tech Stack:** C# / .NET 10、WPF、System.Text.Json、xUnit。YMM4 の DLL は環境変数 `YMM4_PATH` から参照。

**Spec:** `docs/specs/2026-10-24-credit-export-design.md`

## Global Constraints

- ターゲット:`net10.0`(Core / テスト)、`net10.0-windows` + `<UseWPF>true</UseWPF>`(プラグイン)。
- YMM4 の DLL 参照は `$(YMM4_PATH)\YukkuriMovieMaker.Plugin.dll`、`<Private>false</Private>`(出力に含めない)。パスの決め打ち禁止。
- コードコメント・ログ・画面文言は日本語。
- メニュー名:`クレジット自動出力`。出力ファイル名:`クレジット.txt`(ymmp と同じフォルダ、UTF-8 BOM 付き、上書き)。
- 素材種類と出力順:立ち絵 / BGM / 効果音 / 画像 / 動画 / 音声 / フォント / その他。
- 照合:ファイル・立ち絵は絶対パス(大文字小文字無視)、音声は `API` + `Arg`、フォントは名前。付け替え機能なし。
- 個人用スクリプトの登録データは読まない・取り込まない。動作確認は空の登録データから始める。
- 登録データ:プラグイン DLL と同じフォルダの `credit-data.json`。保存は一時ファイルに書いてから置き換える(失敗時に元を壊さない)。
- 未保存の現在プロジェクトは警告して中止。自動保存しない。

## Review Focus

1. 壊れた / JSON でない / 開けない ymmp を選んだ → エラーメッセージを出し、`クレジット.txt` は書かず、リストも変えない(Task 2 にテスト)。
2. 壊れた `credit-data.json` → 読み込み失敗を通知し、**上書き保存しない**(Task 3 にテスト。Task 5 で保存禁止フラグ)。
3. 同じファイルが大文字小文字違いのパスで ymmp と登録データにある → 同じ素材として照合(Task 4 にテスト)。
4. 「まとめる」出典が BGM と効果音にまたがる → 両方の見出しに 1 行ずつ、まとめ文が空なら該当なしへ(Task 4 にテスト)。
5. `クレジット.txt` が他アプリで開かれていて書けない → エラーメッセージ、プラグインは落ちない(Task 6 の手動確認)。

---

## ファイル構成

```
YMM4CreditPlugin.slnx
.gitignore                                  bin/ obj/ *.user
src/YMM4CreditPlugin.Core/
  YMM4CreditPlugin.Core.csproj
  MaterialKind.cs        素材種類の列挙と表示名・出力順
  UsedMaterial.cs        ymmp から抽出した 1 素材
  YmmpParser.cs          ymmp → UsedMaterial 一覧
  CreditDatabase.cs      登録データ(素材エントリ・出典設定)と照合
  CreditDatabaseStore.cs credit-data.json の読み書き
  CreditBuilder.cs       クレジット.txt の本文組み立て
src/YMM4CreditPlugin/
  YMM4CreditPlugin.csproj
  CreditToolPlugin.cs    IToolPlugin
  CurrentProjectLocator.cs 現在のプロジェクトのパスと保存状態
  CreditToolView.xaml(.cs) / CreditToolViewModel.cs  メイン画面
  ExtractSelectionWindow.xaml(.cs)                     抽出時のチェックボックス選択
  RelayCommand.cs
tests/YMM4CreditPlugin.Core.Tests/
  YMM4CreditPlugin.Core.Tests.csproj
  YmmpParserTests.cs / CreditDatabaseTests.cs / CreditBuilderTests.cs
```

---

### Task 1: ソリューションと素材モデル

**Files:**
- Create: `YMM4CreditPlugin.slnx`, `.gitignore`, `src/YMM4CreditPlugin.Core/YMM4CreditPlugin.Core.csproj`, `src/YMM4CreditPlugin.Core/MaterialKind.cs`, `src/YMM4CreditPlugin.Core/UsedMaterial.cs`
- Test: `tests/YMM4CreditPlugin.Core.Tests/MaterialKindTests.cs`

**Interfaces:**
- Produces:
  - `enum MaterialKind { Tachie, Bgm, SoundEffect, Image, Video, Voice, Font, Other }`(宣言順 = 出力順)
  - `static class MaterialKindExtensions { string ToDisplayName(this MaterialKind k); }` → `立ち絵 / BGM / 効果音 / 画像 / 動画 / 音声 / フォント / その他`
  - `enum MaterialKeyType { File, Tachie, Voice, Font }`
  - `sealed record UsedMaterial(MaterialKeyType KeyType, string Key, string DefaultName, MaterialKind GuessedKind)`
    - Key:File/Tachie は絶対パス、Voice は `"{API}|{Arg}"`、Font はフォント名。
  - `static class MaterialKey { StringComparer Comparer; }` = `StringComparer.OrdinalIgnoreCase`(全照合でこれを使う)

- [ ] **Step 1:** `dotnet new sln -n YMM4CreditPlugin`、`dotnet new classlib`(Core, net10.0)、`dotnet new xunit`(Tests, net10.0, Core を参照)。`.gitignore` 作成。
- [ ] **Step 2: 失敗するテスト**

```csharp
[Fact] public void 表示名が仕様どおり() {
    Assert.Equal(new[]{"立ち絵","BGM","効果音","画像","動画","音声","フォント","その他"},
        Enum.GetValues<MaterialKind>().Select(k => k.ToDisplayName()));
}
```

- [ ] **Step 3:** `dotnet test` → コンパイルエラーで FAIL を確認。
- [ ] **Step 4:** `MaterialKind.cs` / `UsedMaterial.cs` を実装。
- [ ] **Step 5:** `dotnet test` → PASS。
- [ ] **Step 6:** commit `ソリューションと素材モデルを追加`

---

### Task 2: ymmp 解析

**Files:**
- Create: `src/YMM4CreditPlugin.Core/YmmpParser.cs`
- Test: `tests/YMM4CreditPlugin.Core.Tests/YmmpParserTests.cs`(ymmp は文字列で組み立てて一時ファイルに書く)

**Interfaces:**
- Consumes: Task 1 の `UsedMaterial` / `MaterialKeyType` / `MaterialKind` / `MaterialKey`
- Produces:
  - `static class YmmpParser { IReadOnlyList<UsedMaterial> Parse(string ymmpPath); }`
  - 失敗時は `YmmpParseException : Exception`(メッセージは日本語、内部例外を保持)。ファイル無し・JSON 不正・`Timelines` 無しのいずれもこれに包む。

抽出ルール(JSON は UTF-8 BOM 付き。`JsonDocument` で読む):
- 対象は全 `Timelines[].Items[]` を再帰的に辿った中身のみ。ルートの `FilePath`(ymmp 自身)は対象外。非表示・無効アイテムも含める。
- 文字列値で、キャラ設定の `TachieCharacterParameter.Directory` 配下のパス → Tachie。Key はそのフォルダ(末尾 `\` なし)、DefaultName はフォルダ名。
- キー名 `FilePath` の文字列値(上記以外)→ File。DefaultName は拡張子なしファイル名。GuessedKind は拡張子で決める:動画 `.mp4 .mov .avi .mkv .webm .wmv .flv` → Video、画像 `.png .jpg .jpeg .gif .bmp .webp .tif .tiff .svg .psd` → Image、それ以外 → Other。
- キー名 `Font` の空でない文字列値 → Font(Key = DefaultName = フォント名)。
- アイテムの `CharacterName` を集め、ルート `Characters[]` で `Name` が一致するキャラについて:
  - `VoiceItem` から参照されていれば Voice(Key `"{API}|{Arg}"`、DefaultName `"{API} / {Arg}"`)。
  - `TachieItem`/`VoiceItem` から参照され、`TachieCharacterParameter.Directory` が空でなければ Tachie(Key = Directory)。
- タイムラインで参照されないキャラは含めない。
- 結果は Key の重複を `MaterialKey.Comparer` で除き、`(KeyType, Key)` 順に並べる。

- [ ] **Step 1: 失敗するテスト**(各 1 Fact)
  - `ファイル素材を拡張子で分類する`:VideoItem `C:\a\rec.mp4` → (File, Video, DefaultName `rec`)、AudioItem `C:\a\bgm.mp3` → (File, Other, `bgm`)、ImageItem `C:\a\x.PNG` → Image。
  - `ルートのFilePathは含めない`:ルート `FilePath` の ymmp パスが結果に無い。
  - `立ち絵フォルダ配下の画像は立ち絵1件になる`:`DefaultFace` = `C:\chara\キャラA\01.png` と `...\02.png` → Tachie 1 件、Key `C:\chara\キャラA`、File は 0 件。
  - `使われているキャラの声と立ち絵`:Characters に 話者B(EngineA/f1, Directory あり)と 未使用(EngineA/f9)、VoiceItem が 話者B → Voice `EngineA|f1` / DefaultName `EngineA / f1` と Tachie が含まれ、`EngineA|f9` は含まれない。
  - `フォントを集め重複を除く`:2 アイテムの `Font` が `フォントA`、1 つが `フォントB` → Font 2 件。
  - `大文字小文字違いのパスは1件`:`C:\A\b.wav` と `c:\a\B.wav` → 1 件。
  - `複数シーンを辿る`:Timelines[1] の素材も含まれる。
  - `壊れたymmpは例外`:中身 `{` のファイル → `YmmpParseException`。存在しないパス → `YmmpParseException`。
- [ ] **Step 2:** `dotnet test --filter YmmpParserTests` → FAIL。
- [ ] **Step 3:** `YmmpParser.Parse` を実装。
- [ ] **Step 4:** `dotnet test` → PASS。
- [ ] **Step 5:** 実データ確認:テストとは別に 手元の実際の動画プロジェクトの ymmp を Parse する使い捨てテスト(`[Fact(Skip=...)]` にはせず、実行後に削除)で、BGM 1 件、録画 mp4 5 件、立ち絵 2 件、Voice 2 件、Font 1 件が出ることを確認する。
- [ ] **Step 6:** commit `ymmp から使用素材を抽出する処理を追加`

---

### Task 3: 登録データと保存

(テストは一時フォルダのファイルだけを使う)

**Files:**
- Create: `src/YMM4CreditPlugin.Core/CreditDatabase.cs`, `src/YMM4CreditPlugin.Core/CreditDatabaseStore.cs`
- Test: `tests/YMM4CreditPlugin.Core.Tests/CreditDatabaseTests.cs`

**Interfaces:**
- Consumes: Task 1 の型
- Produces:
  - `sealed class MaterialEntry { MaterialKeyType KeyType; string Key; string Name; string Source; string Credit; MaterialKind Kind; }`(すべて get/set プロパティ、文字列は既定 `""`)
  - `sealed class SourceSetting { string Name; bool Merge; string MergedCredit; }`
  - `sealed class CreditDatabase`
    - `List<MaterialEntry> Materials`, `List<SourceSetting> Sources`
    - `MaterialEntry? Find(MaterialKeyType type, string key)`(`MaterialKey.Comparer`)
    - `bool Contains(UsedMaterial m)`
    - `MaterialEntry Add(UsedMaterial m)` → Name = DefaultName、Kind = GuessedKind、Source/Credit 空。既にあれば既存を返す。
    - `void SyncSources()` → Materials の空でない Source のうち Sources に無い名前を `Merge=false, MergedCredit=""` で追加。既存の設定は消さない・変えない。
    - `SourceSetting? FindSource(string name)`(大文字小文字は区別する:`StringComparer.Ordinal`)
  - `static class CreditDatabaseStore`
    - `CreditDatabase Load(string path)` → ファイル無しなら空の DB。JSON 不正なら `CreditDatabaseLoadException`。
    - `void Save(string path, CreditDatabase db)` → `path + ".tmp"` に書いてから `File.Move(tmp, path, overwrite: true)`。UTF-8、インデント付き、enum は文字列(`JsonStringEnumConverter`)。

- [ ] **Step 1: 失敗するテスト**
  - `Addは重複しない`:同じ Key(大文字小文字違い)を 2 回 Add → Materials 1 件。
  - `SyncSourcesは新しい出典だけ足す`:既存 Source `出典1`(Merge=true, MergedCredit="X")、Materials に `出典1` と `出典2` → Sources 2 件、出典1は Merge=true / "X" のまま。
  - `保存して読み戻せる`:Save → Load で全フィールド一致、`.tmp` が残らない。
  - `ファイルが無ければ空`:Load(存在しないパス)→ Materials 0 件。
  - `壊れたJSONは例外で元ファイルはそのまま`:中身 `{x` → `CreditDatabaseLoadException`、ファイル内容は `{x` のまま。
- [ ] **Step 2:** `dotnet test --filter CreditDatabaseTests` → FAIL。
- [ ] **Step 3:** 実装。
- [ ] **Step 4:** `dotnet test` → PASS。
- [ ] **Step 5:** commit `クレジット登録データと保存処理を追加`

---

### Task 4: クレジット本文の組み立て

**Files:**
- Create: `src/YMM4CreditPlugin.Core/CreditBuilder.cs`
- Test: `tests/YMM4CreditPlugin.Core.Tests/CreditBuilderTests.cs`

**Interfaces:**
- Consumes: Task 1〜3 の型
- Produces: `static class CreditBuilder { string Build(IReadOnlyList<UsedMaterial> used, CreditDatabase db); }`(改行は `\r\n`、末尾改行あり)

アルゴリズム:

```
各 used について entry = db.Find(...)
  entry なし                 → 該当なし(種類 = used.GuessedKind)
  src = entry.Source が空でなければ db.FindSource(entry.Source)
  src?.Merge == true         → 文面 = src.MergedCredit
  それ以外                    → 文面 = entry.Credit
  文面が空白のみ              → 該当なし(種類 = entry.Kind)
  それ以外                    → entry.Kind の見出しに文面を追加(同じ見出し内で同じ文面は 1 回だけ。最初に出た順)
出力:
  MaterialKind の宣言順に、文面がある種類だけ "■{表示名}" + 文面行
  該当なしがあれば 空行 + "# --- クレジット該当なし ---" + "# [{種類表示名}] {File/Tachie は Key、Voice/Font は DefaultName}"
    (並びは種類の宣言順 → Key 順)
  どちらも無ければ空文字列
```

- [ ] **Step 1: 失敗するテスト**
  - `種類ごとに見出しを付けて並べる`:BGM 1 件「出典名 https://example.com/」、立ち絵 1 件「立ち絵:A」→ `"■立ち絵\r\n立ち絵:A\r\n■BGM\r\n出典名 https://example.com/\r\n"`。
  - `まとめる出典は1行`:効果音 3 件、Source `出典1`(Merge=true, MergedCredit `効果音：出典1`)、各 Credit はばらばら → `■効果音` の下に `効果音：出典1` 1 行だけ。
  - `まとめない出典は素材ごと`:Merge=false の 2 件 → 各 Credit が 2 行。
  - `まとめる出典が種類をまたぐ`:同じ出典の BGM 1 件・効果音 1 件(Merge=true)→ `■BGM` と `■効果音` の両方に同じ 1 行。
  - `同じ文面は見出し内で1回`:Merge=false で Credit が同一の 2 件 → 1 行。
  - `未登録とクレジット空は該当なし`:未登録 File `C:\a\rec.mp4`(GuessedKind=Video)、登録済み Font `フォントB` で Credit 空 → 末尾が `"\r\n# --- クレジット該当なし ---\r\n# [動画] C:\\a\\rec.mp4\r\n# [フォント] フォントB\r\n"`。
  - `まとめ文が空なら該当なし`:Merge=true、MergedCredit 空 → 該当なし。
  - `大文字小文字違いのパスで照合する`:登録 Key `C:\A\b.wav`、used Key `c:\a\B.wav` → クレジットが出る。
- [ ] **Step 2:** `dotnet test --filter CreditBuilderTests` → FAIL。
- [ ] **Step 3:** 実装。
- [ ] **Step 4:** `dotnet test` → PASS。
- [ ] **Step 5:** commit `クレジット本文の組み立てを追加`

---

### Task 5: YMM4 プラグインの土台(メニュー・現在のプロジェクト取得・配置)

**Files:**
- Create: `src/YMM4CreditPlugin/YMM4CreditPlugin.csproj`, `CreditToolPlugin.cs`, `CurrentProjectLocator.cs`, `RelayCommand.cs`, 仮の `CreditToolView.xaml(.cs)` / `CreditToolViewModel.cs`

**Interfaces:**
- Consumes: Core 全体
- Produces:
  - `public class CreditToolPlugin : IToolPlugin` — `Name => "クレジット自動出力"`、`ViewModelType => typeof(CreditToolViewModel)`、`ViewType => typeof(CreditToolView)`。他メンバーは既定実装に任せる(作者の別プラグインと同じ)。
  - `static class CurrentProjectLocator { ProjectState Get(); }`
    - `sealed record ProjectState(string? FilePath, bool IsSaved, string? Error)`
    - `Application.Current.MainWindow.DataContext` から `ProjectFilePath` と `IsSaved` プロパティをリフレクションで読む。値が `Value` プロパティを持つ型(ReactiveProperty 等)なら `.Value` を読む。取れなければ `Error` に日本語の理由を入れる(例外は投げない)。
  - `static string DataFilePath` = `Path.Combine(Path.GetDirectoryName(typeof(CreditToolPlugin).Assembly.Location)!, "credit-data.json")`(`CreditToolPlugin` に置く)

csproj:
- `<Reference Include="YukkuriMovieMaker.Plugin"><HintPath>$(YMM4_PATH)\YukkuriMovieMaker.Plugin.dll</HintPath><Private>false</Private></Reference>`
- Core を ProjectReference。
- ビルド後に `$(TargetDir)` の `YMM4CreditPlugin.dll` と `YMM4CreditPlugin.Core.dll` を `$(PluginDeployDir)` にコピーする Target。`PluginDeployDir` の既定は `$(YMM4_PATH)\user\plugin\YMM4CreditPlugin`。`YMM4_PATH` 未設定ならエラーで止める。

- [ ] **Step 1:** 上記を作成。仮の画面は「現在のプロジェクト: {FilePath} / 保存済み: {IsSaved} / {Error}」を表示するボタン 1 つ。
- [ ] **Step 2:** `dotnet build -c Release` → 成功、`%YMM4_PATH%\user\plugin\YMM4CreditPlugin\` に DLL 2 つ(YMM4 の DLL は含まれない)。
- [ ] **Step 3: 手動確認**(YMM4 を起動。ユーザーに依頼):ツールメニューに「クレジット自動出力」が出てウィンドウが開く。保存済みプロジェクトでボタン → パスと `保存済み: True`。何か編集して押す → `False`。新規(未保存)プロジェクト → パス空。
- [ ] **Step 4:** commit `YMM4 プラグインの土台と現在のプロジェクト取得を追加`

---

### Task 6: メイン画面と抽出選択画面

**Files:**
- Modify: `src/YMM4CreditPlugin/CreditToolView.xaml(.cs)`, `CreditToolViewModel.cs`
- Create: `src/YMM4CreditPlugin/ExtractSelectionWindow.xaml(.cs)`

**Interfaces:**
- Consumes: `YmmpParser.Parse`、`CreditDatabase`、`CreditDatabaseStore`、`CreditBuilder.Build`、`CurrentProjectLocator.Get`、`CreditToolPlugin.DataFilePath`
- Produces: なし(最終画面)

画面(上から):
1. 「現在のプロジェクトから出力」「ymmp を選んで出力」
2. 「現在のプロジェクトから抽出」「ymmp から抽出」
3. 素材リスト(DataGrid、複数選択可):素材名 / 出典 / クレジット / 素材種類(ComboBox、表示名)。キー(パス等)は読み取り専用列またはツールチップで見せる。下に一括編集欄(出典・クレジット・種類 + 「選択行に適用」、空欄の項目は変更しない)と「選択行を削除」。
4. 出典一覧(DataGrid):出典名(読み取り専用) / まとめる(CheckBox) / まとめ用クレジット。

挙動:
- 起動時に `CreditDatabaseStore.Load`。`CreditDatabaseLoadException` なら MessageBox で通知し、この画面では**保存を禁止**(編集はできても保存しない旨を表示)。
- 編集(セル確定・一括適用・削除・抽出追加・出典設定変更)のたびに `SyncSources` → `Save`。保存失敗は MessageBox。
- 対象 ymmp の決定:現在のプロジェクト → `CurrentProjectLocator.Get()`。`Error` あり / `FilePath` 空 → 「プロジェクトを保存してから実行してください」等で中止。`IsSaved == false` → 「未保存の変更があります。保存してから実行してください」で中止。ymmp 選択 → `Microsoft.Win32.OpenFileDialog`(Filter `YMM4 プロジェクト (*.ymmp)|*.ymmp`)。
- 抽出:`Parse` → `db.Contains` で登録済みを除外 → 0 件なら「新しい素材はありません」→ それ以外は `ExtractSelectionWindow`(チェックボックス・種類・素材名・キー、全選択/全解除、初期は全チェック)で OK した分を `db.Add`。
- 出力:`Parse` → `Build` → `Path.Combine(ymmpのフォルダ, "クレジット.txt")` に UTF-8 BOM で書く → 「出力しました: {パス}」。`YmmpParseException` / IO 例外は MessageBox で理由を出す。

- [ ] **Step 1:** 画面と ViewModel を実装し、`dotnet build -c Release` → 成功。
- [ ] **Step 2: 手動確認**(ユーザーに依頼。チェック項目を提示する)
  - 手元の動画プロジェクトの ymmp で「ymmp から抽出」→ 選択画面に BGM・録画・立ち絵 2・声 2・フォントが出る。録画のチェックを外して OK → 録画以外がリストに入る。もう一度抽出 → 「新しい素材はありません」。
  - 2 行選んで出典 `テスト出典` を一括適用 → 出典一覧に `テスト出典` が出る。まとめるオン + まとめ文入力。
  - 「ymmp を選んで出力」→ ymmp と同じフォルダに `クレジット.txt`、見出し・まとめ・該当なし(録画 5 件)が仕様どおり。
  - YMM4 を再起動してもリストと出典設定が残っている(`credit-data.json`)。
  - `クレジット.txt` をメモ帳以外で排他的に開いた状態(例:Excel)で出力 → エラーメッセージ、落ちない。
  - 現在のプロジェクトで、編集して未保存 → 警告で中止。保存後 → 出力できる。
- [ ] **Step 3:** commit `メイン画面と抽出選択画面を追加`

---

## 範囲外

- 作者の環境への組み込み(DLL や登録データの管理)。
