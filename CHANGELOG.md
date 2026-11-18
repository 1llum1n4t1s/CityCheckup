# Changelog

City Checkup（Cities: Skylines II 用 mod）の変更履歴です。各版の節は Paradox Mods の変更履歴（ChangeLog）にもそのまま使われるため、
英語と日本語の両方で書きます（`mod/scripts/Release-ParadoxMods.ps1` が `## [版]` の節を取り出して送ります）。

## [1.0.1] - 2026-11-18

- New DLC tab: shows which DLC your next save will require and why, and after you disable a DLC, removes the references (vehicles, props, brands, companies, line vehicle settings) so the save no longer requires it.
- The DLC tab can also clear the "this save uses mods" record.
- Load analysis now also counts citizens that other mods (such as Big City Performance) put to sleep, so unemployment is no longer underestimated.
- Missing assets: assets whose name cannot be read are now shown in the panel language.
- 新しい「DLC」タブ：次の保存でセーブが必要とする DLC とその原因を表示し、DLC を無効にした後に参照（車両、付属物、ブランド、会社、路線の車両指定）を取り除いて、セーブが DLC を必要としないようにできます。
- DLC タブで、セーブに付く「MOD を使用しています」の記録も消せます。
- 負荷分析：ほかの mod（Big City Performance など）が一時的に止めた市民も数えるようにし、失業者が少なく出ないようにしました。
- 欠落アセット：名前を読めないアセットを、パネルの言語で表示するようにしました。

## [1.0.0] - 2026-11-12

- First stable version. No functional changes from 0.1.0.
- 正式版。0.1.0 からの機能の変更はありません。

## [0.1.0] - 2026-11-12

- First release on Paradox Mods (ID 167225).
- Load analysis: splits the slowdown into waiting for pathfinding and simulation frame time, shows pathfinding work and success rate per request type, and lists districts short of jobs.
- Missing assets: finds objects left by uninstalled mods, jumps to them and deletes the selected ones.
- The panel follows the game language (Japanese or English).
- 初回公開。負荷分析（遅れの原因の切り分け・経路探しの作業量・職の足りない地区）と、欠落アセットの検出・削除。ゲームの言語に合わせて日本語／英語で表示。
