using Game;
using Game.Modding;
using CityCheckup.Analysis;
using CityCheckup.Dlc;
using CityCheckup.Missing;
using CityCheckup.Ui;
using UnityEngine;

namespace CityCheckup;

/// <summary>
/// mod の入口。ゲームが Mods フォルダーの DLL を読み込むと OnLoad が呼ばれ、ここで自前のシステムを登録する。
/// </summary>
/// <remarks>
/// ゲームの処理を変えるシステムはここで登録しない方針。分析・表示は読み取り専用、
/// 削除・付け替えはプレイヤーが UI で選んだときだけ行う。
/// </remarks>
public sealed class Mod : IMod
{
    /// <summary>Player.log でこの mod の行を見分けるための接頭辞。</summary>
    public const string LogPrefix = "[CityCheckup]";

    public void OnLoad(UpdateSystem updateSystem)
    {
        // UI のバインディングは UIUpdate の段で更新する（ゲーム本体の UI システムと同じ段）。
        updateSystem.UpdateAt<PanelUISystem>(SystemUpdatePhase.UIUpdate);
        // 欠落アセットの検出と削除。探す・消すはプレイヤーが UI で選んだときだけ動く。
        updateSystem.UpdateAt<MissingAssetSystem>(SystemUpdatePhase.UIUpdate);
        // セーブが DLC を要求する原因の診断（読み取り専用）。目印ファイル debug-dlc があるときだけ動く。
        updateSystem.UpdateAt<DlcDiagnosticSystem>(SystemUpdatePhase.UIUpdate);
        // 「DLC」タブ。DLC を要求する原因を探し、プレイヤーが選んだときだけ取り除く（削除・ブランドの付け替え・路線の車両指定の解除）。
        updateSystem.UpdateAt<DlcRemovalSystem>(SystemUpdatePhase.UIUpdate);
        // 速度の内訳と経路探しの作業量を、画面のフレームごとに読む（パネルを開いている間だけ）。
        // シミュレーションの後に動く UIUpdate の段で読むので、そのフレームの frameDuration が取れる。
        updateSystem.UpdateBefore<LoadProbeSystem, PanelUISystem>(SystemUpdatePhase.UIUpdate);
        Debug.Log($"{LogPrefix} loaded");
    }

    public void OnDispose()
    {
    }
}
