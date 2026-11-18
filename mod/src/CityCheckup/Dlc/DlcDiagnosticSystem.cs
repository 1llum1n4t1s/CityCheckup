using System;
using System.Diagnostics;
using Colossal.IO.AssetDatabase;
using Colossal.Serialization.Entities;
using Game;
using Game.Assets;
using Game.Prefabs;
using Game.Serialization;
using Game.UI;
using Debug = UnityEngine.Debug;

namespace CityCheckup.Dlc;

/// <summary>
/// セーブが DLC などのコンテンツを要求する原因を列挙して Player.log に書き出す、読み取り専用の診断。
/// </summary>
/// <remarks>
/// 目印ファイル debug-dlc（DebugMarkers）があるときだけ、読み込み完了の数秒後に 1 回動く。目印が無ければ何もしない。
/// ゲームは保存時に PrimaryPrefabReferencesSystem が街から参照される prefab に印を付け、
/// SecondaryPrefabReferencesSystem がその prefab の ContentPrerequisiteData / ModPrerequisiteData が指すコンテンツに印を付ける。
/// その照会（Primary の各経路と、公開されているシステムの参照）を同じ条件で読む処理は DlcScanner にまとめ、参照先がコンテンツへつながるものだけを
/// 保有 Entity の種類・参照先が欠落しているか・処置の分類つきで Core の DlcDiagnostics に数えさせる。
/// 判定・集計・文面は Core が受け持ち、この層は照会して渡すだけ。Entity とコンポーネントは読むだけで、何も書き換えない。
/// </remarks>
public sealed partial class DlcDiagnosticSystem : UISystemBase
{
    // 読み込み完了からこの秒数だけ待ってから動かす（読み込み直後の初期化と重ならないように）。
    private const float DelaySeconds = 5f;

    private LoadGameSystem m_LoadGame = null!;
    private DlcScanner m_Scanner = null!;
    private PrefabSystem m_Prefabs = null!;

    private bool m_Pending;
    private float m_RunAt;

    public override GameMode gameMode => GameMode.Game;

    protected override void OnCreate()
    {
        base.OnCreate();
        m_LoadGame = World.GetOrCreateSystemManaged<LoadGameSystem>();
        // 照会の条件は DlcScanner にまとめてある（DLC タブ・取り除きと共有）。
        m_Prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
        m_Scanner = new DlcScanner(World, m_Prefabs, desc => GetEntityQuery(desc));
    }

    protected override void OnGamePreload(Purpose purpose, GameMode mode)
    {
        base.OnGamePreload(purpose, mode);
        m_Pending = false;
    }

    protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
    {
        base.OnGameLoadingComplete(purpose, mode);
        // 目印が無ければ何も予約しない（重い走査を常時回さない）。
        m_Pending = mode == GameMode.Game && DebugMarkers.Has("debug-dlc");
        m_RunAt = UnityEngine.Time.realtimeSinceStartup + DelaySeconds;
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();
        if (!m_Pending || UnityEngine.Time.realtimeSinceStartup < m_RunAt)
        {
            return;
        }
        m_Pending = false;
        try
        {
            Run();
        }
        catch (Exception e)
        {
            Debug.LogError($"{Mod.LogPrefix} dlc diagnostic failed: {e}");
        }
    }

    private void Run()
    {
        var watch = Stopwatch.StartNew();
        var diagnostics = m_Scanner.Scan();

        var saveContent = ReadSaveContent(out var note);
        foreach (var line in diagnostics.BuildLines(saveContent, note))
        {
            Debug.Log($"{Mod.LogPrefix} {line}");
        }
        // 取り除いた後に保存し直したセーブに、宙に浮いた参照が持ち越されていないかも見る（読むだけ）。
        foreach (var line in DanglingReferences.Describe(EntityManager, m_Prefabs, m_Scanner.CompanyQuery))
        {
            Debug.Log($"{Mod.LogPrefix} dlc {line}");
        }
        Debug.Log($"{Mod.LogPrefix} dlc diagnostic took {watch.ElapsedMilliseconds} ms");
    }

    /// <summary>
    /// いま読み込んでいるセーブのメタデータ（保存時に記録された contentPrerequisites）を読む。
    /// 読み込みの文脈（LoadGameSystem.context）が持つ元の資産の ID から SaveGameMetadata を引く。
    /// </summary>
    private string[]? ReadSaveContent(out string? note)
    {
        try
        {
            var guid = m_LoadGame.context.instigatorGuid;
            if (!guid.isValid)
            {
                note = "load context has no source asset id (new game or non-save load)";
                return null;
            }
            // 型引数に SaveGameMetadata を使う GetAsset<T> は、参照 DLL に無い Colossal.AssetPipeline の型を要求して
            // ビルドできない。型を伴わない TryGetAsset で引き、メタデータの本体（target）は反射で読む。
            if (!AssetDatabase.global.TryGetAsset(guid, out IAssetData asset) || asset is null)
            {
                note = $"asset {guid} not found in AssetDatabase";
                return null;
            }
            var info = asset.GetType().GetProperty("target")?.GetValue(asset) as IContentPrerequisite;
            if (info is null)
            {
                note = $"asset {guid} ({asset.GetType().Name}) has no SaveInfo target";
                return null;
            }
            if (info.contentPrerequisites is null)
            {
                note = "SaveInfo.contentPrerequisites is null";
                return null;
            }
            note = null;
            return info.contentPrerequisites;
        }
        catch (Exception e)
        {
            note = $"{e.GetType().Name}: {e.Message}";
            return null;
        }
    }
}
