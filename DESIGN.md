# DESIGN.md

City Checkup の設計。作業規約とコマンドは [AGENTS.md](AGENTS.md)、利用手順は [README.md](README.md)。

## 目的

Cities: Skylines II の街が重い原因を見える化し、アンインストールした mod の置き土産と、外した DLC を要求し続ける原因を安全に片付ける。
製品の主軸はゲーム内のコード mod（`mod/`）。`.cok` セーブをオフラインで調べて整理する Rust ツール（`src/`）は、mod 以前の成果として残している補助ツール。

## mod（`mod/`）

### 構成と責務

| 部品 | 責務 |
| --- | --- |
| `Mod`（`IMod`） | システムの登録だけを行う。ゲームの処理は変えない。 |
| `CityCheckup.Core` | ゲーム型に依存しない判定・集計・JSON 生成。`LoadReport`（所見の判定と JSON）、`JobMarket`（学歴別の求職者と空き職場、地区ごとの失業）、`PathfindSources`（システム名から依頼元の分類と区間の差分）、`SpeedSampler`（速度が設定より遅い理由を、経路探し待ちと 1 フレームの処理時間に分けて集計）、`MissingAssets`（欠落アセットの集約）、`DlcDiagnostics`（DLC を要求する参照の集計と次の保存での予測）・`DlcRemoval`（処置の表、ブランドの付け替え先の選択、路線の車両指定の書き換え）・`DlcView`（DLC タブの表示単位と JSON）、`UiLanguage`（ゲームのロケールから日本語／英語を決める）、`JsonText`。 |
| `Analysis/CityScanner` | 市民・世帯・職場をたどって `JobMarket` と世帯の集計を作る（読み取り専用）。 |
| `Analysis/LoadProbeSystem` | 画面のフレームごとに、ゲーム自身の公開値（`SimulationSystem` の速度・1 フレームの処理時間、`PathfindResultSystem` の結果待ちと `queryStats`）を読む。区間の始めと終わりの `queryStats` の差から、依頼元システムごとの件数・成功数・探索の作業量を出す。 |
| `Ui/PanelUISystem` | 負荷分析パネルのバインディング（グループ `cityCheckup`）。開いている間だけ集計を動かし、結果を JSON 文字列 1 本で UI へ渡す。表示言語（`locale` バインディング）も毎秒ゲームの言語設定から読んで渡す。 |
| `Ui/CameraJump` | 「見に行く」でカメラを移す。 |
| `Missing/MissingAssetSystem` | 欠落プレハブを参照する物を探し、選ばれたものを削除する。 |
| `Common/EntityRules` | 物の種類・単独で消してよいか・位置の判定（欠落アセットと DLC で共有）。 |
| `Dlc/DlcScanner` | 保存時の `PrimaryPrefabReferencesSystem` と同じ条件で参照を照会し（システムが持つ参照のうち公開されているものを含む）、保有 Entity の種類・参照先の欠落・処置で分類して `DlcDiagnostics` に数えさせる（読み取り専用）。付属物の組み直しを頼む相手（最上位の持ち主）の判定も持ち、取り除きと共有する。 |
| `Dlc/DlcRemovalSystem` | DLC タブのバインディング（`dlc`・`scanDlc`・`jumpDlc`・`removeDlc`）。選ばれたコンテンツの参照を取り除く。 |
| `Dlc/DlcDiagnosticSystem` | 目印 `debug-dlc` があるときだけ、診断をログに書く（読み取り専用）。 |
| `mod/ui` | タブ付きパネル（負荷分析／欠落アセット／DLC）。文言は `i18n.ts` に日英を持つ。Bun で `CityCheckup.mjs` にビルドし、`GameTopLeft` に登録する。 |

### データフロー

1. 負荷分析: パネルを開く → `PanelUISystem` が `LoadProbeSystem` の収集を有効にし、実時間 1 秒ごとに速度を更新、10 秒ごとに `CityScanner` の走査と `LoadProbeSystem` の区間の締め（経路探しの作業量・速度の内訳）を実行 → Core の `LoadReport` が所見つきの JSON にする → UI が `JSON.parse` して表示。
2. 欠落アセット: 「探す」→ PrefabData が無効化されたプレハブ（読み込み時に見つからなかったもの）を集め、`PrefabRef` で参照する物を数える → 元の名前は `PrefabSystem` の旧 ID から引く → UI が名前ごとに一覧表示 → 選択して削除すると `EndFrameBarrier` で `Deleted` を付ける。
3. DLC: 「探す」→ `DlcScanner` が保存時と同じ照会で、コンテンツ（`ContentData`）へ `ContentPrerequisiteData` / `ModPrerequisiteData` でつながる prefab への参照を数える → Core がコンテンツ → カテゴリ（処置）にまとめ、次の保存で要求される予測と、取り除いた後も残る予測を出す → 「このコンテンツを取り除く」（2 段階の確認）→ その時点で照会し直し、ブランドの付け替え → 付属物（とその子孫）の削除と最上位の持ち主への `RentersUpdated` → 建物・置き物の削除 → 車両を編成ごと削除 → 企業 prefab が欠落した企業の転出 → 路線の車両指定の書き換え、の順に行う。10 フレーム後に、取り除きの後で新しく持ち主を失った付属物を片付け、さらに 2 フレーム後に探し直す。

### 不変条件

- 分析・表示は読み取り専用。ゲームの状態を変えるのは、プレイヤーが UI で選んだときの削除・付け替え（欠落アセットの削除と、DLC の参照の取り除き）だけ。
- DLC で取り除くのは、参照先の prefab が欠落している（DLC を外して読み込んだ）ものだけ。DLC が有効なままだと、ゲームが車両の選び直し・付属物の組み直し・企業の初期化で同じ prefab を選び直すので、一覧で「無効にして読み込み直す」と案内するだけにする。
- 削除・付け替えは `EndFrameBarrier` のコマンドバッファで行う（ゲームの情報パネルの「削除」と同じ）。UIUpdate の段で `EntityManager` から直接 `Deleted` を付けると、同じフレームの後片付けで消えて参照が宙に浮く。例外は路線の車両指定で、ゲームの UI と同じくメインスレッドで直接書き換える。
- 重い処理（全市民の走査、全物体の探索）は、パネルの該当タブを開いている間か明示操作の間だけ動く。
- 削除可否は、ゲームの情報パネルの「削除」ボタンの条件に合わせる（置き物は持ち主のいない静的な物、建物は付属建物でないもの。成長型の建物は欠落していれば作り直されないので削除可）。道路・線路・エリアはゲームのツールを通す必要があるため、一覧と「見に行く」だけで削除しない。
- 所見は実測値から言えることだけにする。mod で測っていない値（CPU 使用率など）は推測で書かない。
- 職探しの対象条件は、ゲームの `CitizenFindJobSystem` と同じにそろえる（世帯に属し、働いておらず、学生・病気でなく、子どもと高齢者を除く）。観光客と転出中の世帯は除く。
- C# のバインディング名と `mod/ui/src` の名前は一致させる。

### 設計判断とトレードオフ

- **Core を分離しソースごと取り込む**: ゲーム型を使わない部分を `net11.0` の MSTest で検証でき、配布は DLL 1 つで済む。代わりにゲーム側とのつなぎ（バインディング・システム）はゲームを起動しないとテストできない。
- **`netstandard2.1`・`NoStdLib`・ゲームの `Managed` DLL を参照**: Unity Mono が使う型と揃えるため。ビルドにゲームの DLL が要る。PublishAot は使わない。
- **JSON 文字列 1 本のバインディング**: 値の追加時に C# と UI の両方でバインディングを増やさずに済む。UI 側で毎回パースする。
- **経路探しはゲーム自身の統計で測る**: `PathfindResultSystem.queryStats`（公開）の区間差分を使い、件数ではなく探索の作業量（`m_GraphTraversal`）で重さを見る。以前は反射で `PathfindSetupSystem` の非公開フィールドを読んで件数を数えていたが、ゲームの更新に弱く作業量も取れないため廃止した（Git の `61bc654` に実装が残る）。限界として、統計は依頼元のシステム単位なので「失業者の職探し」と「転職」は分けられない。
- **速度低下の原因を実測で 2 つに分ける**: ゲームが速度を落とす理由は「経路探しの結果待ち」と「1 フレームの処理時間が設定速度の間隔を超える」の 2 つで、どちらもゲームの公開値から同じ式で求める。CPU 使用率のような mod で測っていない値は出さない。
- **選択中はパネルを畳む**: 「見に行く」で選択するとゲームの情報パネルが開き、重なるため。
- **UI は日本語と英語**: ゲームの言語設定（`activeLocaleId`）が `ja` で始まれば日本語、それ以外はすべて英語。言語は途中で変えられるので毎秒読み直す。所見の文は Core の `LoadReport.Language` で出し分け、UI の文言は `mod/ui/src/i18n.ts` に置く。掲載文（`PublishConfiguration.xml`）は英語と日本語を併記する。
- **DLC の取り除きはカテゴリごとに処置を変える**: 車両は `VehicleUtils.DeleteVehicle` で編成ごと消し、持ち主・乗客・路線の後始末はゲームの参照整理に任せる。看板などの付属物は消して、最上位の持ち主（直接の持ち主から、`Object`＋`Owner` で建物・施設の拡張・車両・人でない中間の付属物の間は上へたどった先）へ `RentersUpdated` を 1 回だけ送り、ゲームに組み直させる。ゲームは中間の付属物を単独の持ち主として組み直さず（`SubObjectSystem.CheckSubObjectOwnersJob`）、入れ子は最上位の組み直しの中で最上位の文脈のまま深さ順に組み直す（`UpdateSubObjectsJob` の DeepOwners）一方、`RentersUpdated` の受け口は受け取った Entity をたどらずに最上位として組み直す。中間へ送ると、その子が最上位の文脈なしで作り直され、同じフレームに最上位も組み直されると中間ごと消されて新しい子が持ち主を失うおそれがあるため。受け手は `HasBuffer` で守られているので、道路など建物以外の最上位でも安全。ただし最上位の持ち主の prefab が欠落していて消されない（取り除かれない道路、単独で消せない欠落建物など）付属物は一覧だけにする（欠落した持ち主を組み直させると、基本ゲームの付属物まで作り直しの対象になるため）。付属物の子孫（SubObject バッファで `Owner` が親を指す物）も一緒に消す。持ち主の組み直しで子まで消える見込みだが、実機で持ち主の消えた `Owner` が残ったための保険で、ゲームの連鎖と結果は同じ。さらに保険として、取り除きの直前に「持ち主が Null・存在しない静的な付属物」を控え、10 フレーム後に新しく持ち主を失った物（建物・施設の拡張・車両・人・`Temp`・`Deleted` を除く）とその子孫だけを、同じ操作の後始末として `EndFrameBarrier` で `Deleted` にする（件数は `orphansCleaned`）。持ち主に `Deleted` が付いたときのゲームの結果（子を全部消す）と同じで、持ち主の SubObject バッファからの除去は `TryGetBuffer` で守られている。取り除きの前から持ち主の無い物には触らない。企業 prefab そのものが欠落した企業は、ゲームの転出（`CompanyMoveAwaySystem` の `MovingAwayJob`）と同じく、物件に `PropertyToBeOnMarket` と `RentersUpdated` を送って企業に `Deleted` を付ける（`MovingAway` を付けて任せる方法は、約 1024 フレームおきにしか動かず、転出の実行の照会が `ProcessingCompany` と `PropertyRenter` を必須にするので使わない。抽出企業を除いているのは転出の判定側の照会だけで、実行側は除かない）。従業員・通知アイコン・物件の入居者一覧・持っている車両の後始末はゲーム（`RemovedSystem`・`SubElementDeleteSystem`）に任せ、建物は消さない。その企業のブランドの参照も一緒に消えるので、付け替え先が無くても残りに数えない。企業のブランドは企業 prefab の候補から付け替える。候補はまず、どのコンテンツへの前提も持たないブランド（基本ゲーム）から選び、無いときだけ欠落したコンテンツへの前提を持たないブランドから選ぶ（有効な別の DLC のブランドへ付け替えて、新しい要求を作らないため）。選び方は企業ごとの種で決定的にする。候補が無ければ `Entity.Null` にせず残す（名前表示が壊れるため）。道路・エリア・方針・予算・チャープ・建設中の次の prefab・`SubReplacement`・`EditorContainer` は一覧だけ。取り除きが途中で例外になっても、それまでに積んだ変更は反映されるので、途中までの件数と `failed` を UI へ渡して知らせる。
- **システムが持つ参照も数える（一覧だけ）**: 保存時の印付けは Entity の参照に加えて、システムが持つ参照にも付く（`PrimaryPrefabReferencesSystem.PatchSystemReferences`。1 回目の `CheckPrefabReferencesSystem` より前なので `ContentPrerequisiteData` の伝播にも乗る）。公開されている街のテーマ（`CityConfigurationSystem.defaultTheme`・`loadedDefaultTheme`）、マップが要求するコンテンツ（`requiredContent`。ContentPrefab そのもので、読み込んだセーブかマップエディターが書く）、気候（`ClimateSystem.currentClimate`）、地形と水の描画設定、建物 prefab ごとの輸送の記録（`TransportUsageTrackSystem`）を読み、カテゴリ「街の設定・統計の記録」として予測（今と取り除き後）に入れる。どれも取り除かない。輸送の記録のキーは輸送の実績による解除条件（`TransportRequirementSystem`）が読むので消さない。天気の効果（`ClimateSystem` の `m_CurrentWeatherEffects` など）は非公開で読めないので数えない（今の気候 prefab の既定の天気から選ばれる）。保存時の Secondary の伝播は `ContentPrerequisiteData`・`ModPrerequisiteData` だけを再現し、成長型建物の区画・代替建物・サービス・レーン・路線の prefab 間のつながりはたどらない。
- **DLC の一覧は保持しない**: 車両は常に増減するので、取り除くときはボタンを押した時点で照会し直す。UI の JSON へは件数・参照先の名前（カテゴリごとに 3 つ）と「見に行く」先の件数だけを渡し、「見に行く」先そのもの（カテゴリごとに最大 200 件）は C# 側の一覧（`DlcRemovalSystem.m_View`）が持つ。
- **取り残し車両の削除は外した**: 2 本のセーブで該当 0 件で、現行ゲームで修正済みと見られるため（Git の `da84f54` に実装が残る）。
- **ゲームの更新に弱い箇所**: システム名による分類（2026-11 時点で確認。名前が変わっても `Other` に入るだけで集計は壊れない）、`queryStats`・`SimulationSystem` の公開値、バージョン指定 `GameVersion`（1.6.*）。

## Rust ツール（`src/`、クレート `cok-inspector`）

`.cok`（ZIP）内の `.SaveGameData`（zstd のフレーム列）を読み、不要な車両などの削除候補を示し、別名の `.cok` に書き出す。

| モジュール | 責務 |
| --- | --- |
| `archive` | `.cok` を開き、項目一覧と外部 `.cid` を扱う。 |
| `save_data` | `SaveGameData` の解析、削除候補の判定（`clean`）、各種試験コピーの作成と照合（`*_trial`、`position`）。 |
| `capture` | ゲーム側で採取した参照記録（`SCR1`）の読み取り。フレームの指紋と、参照 ID の位置・値。 |
| `remap` | 削除後の連続した ID への再割り当て（`EntityRemap`）。 |
| `vehicle_selection` | 旧形式の車両のうち、スポナーに再生成経路のある候補の選別。 |
| `windows` | Win32 の GUI（セーブを開く、保存の準備、別名で保存）。 |
| `src/bin/*` | CLI。`clean-beartris`（整理）、`vehicle-trial`・`position-trial`（試験用）。 |

### 不変条件

- 入力セーブは書き換えない。出力は別名の `.cok` と `.cok.cid`、出力先が既にあれば中止する。新規アセット ID は 32 文字の 16 進数で、4 つ互いに異なる。
- 削除は、対応するレコード形式で、行内の全車両が再生成条件を満たす場合に限る。市民・世帯と重なる候補、分割に未対応のコンポーネント、参照記録との不一致があれば書き出さない。
- 参照の書き換え前に、フレームの指紋だけでなく記録した位置の実バイトも照合する（指紋は候補を絞るためだけに使う）。

### 設計判断とトレードオフ

- **ゲーム自身の参照記録を使う**: セーブ形式をリバースして独自に参照位置を推測するより安全だが、対象セーブごとに `tools/reference-capture` で記録を採取する手間がかかる。
- **ゲームの `PipelinePlugin.dll` を使う**: 圧縮済みの形状の展開にゲーム版に合う実装が要る。ゲーム版ごとに対応する DLL を指定する。
- **GUI は Win32 を直接叩く**: 依存を `zip` と `zstd` だけに抑えるため。Windows 専用。
- **mod 優先**: ゲーム内で状況が見えるほうが扱いやすいと判断し、製品の主軸を mod へ移した。Rust ツールは追加の機能を足さない。

## `tools/`

- `pathfind-count`: 経路探しの依頼元を数える計測専用の一時 mod。初期の原型（反射で数える方式）。現在の mod は公開の統計を使う `LoadProbeSystem` に置き換えており、参考として残している。
- `reference-capture`: ゲームの `Colossal.Core.dll` に計測を差し込む Patcher と計測コード。Rust ツールが使う参照記録と `.stride` を出力する。
