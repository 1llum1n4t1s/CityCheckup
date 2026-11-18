using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Colossal.Serialization.Entities;
using Colossal.UI.Binding;
using Game;
using Game.Buildings;
using Game.City;
using Game.Common;
using Game.Companies;
using Game.Prefabs;
using Game.Rendering;
using Game.Routes;
using Game.Simulation;
using Game.Tools;
using Game.UI;
using Game.UI.InGame;
using Game.Vehicles;
using CityCheckup.Common;
using CityCheckup.Core;
using CityCheckup.Ui;
using Unity.Collections;
using Unity.Entities;
using Debug = UnityEngine.Debug;

namespace CityCheckup.Dlc;

/// <summary>
/// 「DLC」タブ。セーブが DLC を要求する原因になっている参照を一覧にし、プレイヤーが選んだときだけ取り除く（削除・付け替え）。
/// </summary>
/// <remarks>
/// 取り除くのは、DLC を外して読み込んだ（参照先の prefab が欠落した）ものだけ。DLC が有効なままだと、ゲームが車両の選び直し・
/// 付属物の組み直し・企業の初期化で同じ prefab を選び直すため、一覧で「DLC を無効にして読み込み直す」と案内するだけにする。
///
/// 1 回の操作の順序（どれも EndFrameBarrier のコマンドバッファに積み、ゲームの情報パネルの「削除」ボタンと同じくフレームの終わりに反映する。
/// UIUpdate の段で EntityManager から直接 Deleted を付けると、同じフレームの後片付けで消えて参照が宙に浮くため）:
///  1. 企業のブランドを付け替え、その企業の物件へ RentersUpdated を送る（看板を新しいブランドで組み直させる）。
///     企業 prefab そのものが欠落した企業は付け替えず、5 で転出させる。
///  2. 看板などの付属物とその子孫に Deleted を付け、最上位の持ち主（建物・施設の拡張・道路など。DlcScanner.TopOwner）へ
///     RentersUpdated を 1 回だけ送る（SubObjectSystem が最上位の文脈で入れ子ごと組み直す）。最上位の持ち主の prefab が欠落していて
///     消されない付属物は、組み直しで基本ゲームの付属物まで作り直させないよう、触らない（一覧だけ）。
///     子孫も自分でたどって消す（持ち主の組み直しで消える見込みだが、実機で宙に浮いた Owner が残ったための保険。DlcScanner.CollectSubObjectTree）。
///  3. 建物・持ち主のない置き物に Deleted を付ける（「欠落アセット」タブと同じ判定で消してよいものだけ）。
///  4. 車両を編成ごと消す（VehicleUtils.DeleteVehicle）。持ち主・乗客・路線などの後始末はゲームの ReferencesSystem が行う。
///  5. 企業 prefab が欠落した企業を転出させる（物件を空き物件にして企業に Deleted。ゲームの転出と同じ手順。物件は消さない）。
///  6. 路線の車両指定（VehicleModel）から欠落した prefab を外す（ゲームの「選択を外す」と同じ。メインスレッドで直接書き換える）。
///  7. 10 フレーム待ち、取り除きの後に新しく持ち主を失った付属物を片付け（CleanNewOrphans）、さらに少し待ってから探し直す。
/// 道路・エリア・方針・予算・チャープ・街の設定と統計の記録などは一覧だけ。走査の一覧は保持せず、ボタンを押した時点で照会し直す
/// （車両は常に増減するので、数万件の ID を JSON に載せたり預かったりしない）。
/// 走査は数百 ms かかるので、「探す」「取り除く」の操作のときだけ動かす。
/// </remarks>
public sealed partial class DlcRemovalSystem : UISystemBase
{
    private const string Group = PanelUISystem.Group;

    // 取り除きを頼んでから探し直すまでのフレーム数（MissingAssetSystem と同じ）。EndFrameBarrier の反映と後始末を待つ。
    private const int RescanDelayFrames = 10;

    // 持ち主を失った付属物を片付けてから探し直すまでのフレーム数（片付けの Deleted が EndFrameBarrier で反映されるのを待つ）。
    private const int OrphanRescanDelayFrames = 2;

    private DlcScanner m_Scanner = null!;
    private PrefabSystem m_Prefabs = null!;
    private CameraUpdateSystem m_Camera = null!;
    private SelectedInfoUISystem m_SelectedInfo = null!;
    private SimulationSystem m_Simulation = null!;
    private EndFrameBarrier m_EndFrameBarrier = null!;
    private EntityArchetype m_RentersUpdatedArchetype;
    private ValueBinding<string> m_Binding = null!;

    // 「MOD 使用の記録」（CityConfigurationSystem.usedMods）。消した後の案内のため、消したことと件数を控える。
    private CityConfigurationSystem m_CityConfiguration = null!;
    private ValueBinding<string> m_UsedModsBinding = null!;
    private bool m_UsedModsCleared;
    private int m_UsedModsClearedCount;

    // 動作確認用（debug-clear-mods）。読み込み完了から少し待って、記録をログに出して消す。0 なら予定なし。
    private float m_DebugClearModsAt;

    private IReadOnlyList<DlcContentView> m_View = Array.Empty<DlcContentView>();
    private IReadOnlyList<string> m_Predicted = Array.Empty<string>();
    private IReadOnlyList<string> m_PredictedAfter = Array.Empty<string>();
    private DlcRemovalResult? m_Last;
    private string m_State = "idle";
    private bool m_InGame;
    private int m_RescanCountdown;

    // 持ち主の無い（Null・存在しない）静的な付属物。取り除きの直前に控え、探し直しの前に「新しく」持ち主を失った物だけを片付ける。
    // null なら片付けの予定なし。取り除きを続けて頼まれても、片付けが済むまでは控え直さない（1 回目の取り残しを「元から」扱いにしないため）。
    private HashSet<Entity>? m_OrphansBefore;
    private EntityQuery m_OrphanQuery;

    // 動作確認用（debug-dlc-remove）の進行。目印は読み込み完了時に 1 回だけ見る（通常の利用で毎フレームファイルを調べないため）。
    private bool m_DebugEnabled;
    private int m_DebugStep;
    private float m_DebugAt;
    private uint m_DebugFrame;

    // 動作確認の間に Player.log へ出た例外の数。EndFrameBarrier でのコマンドの反映や、ゲームの後始末の中の例外は
    // こちらの try/catch では捕まらないので、ログの受け口で数える。
    private int m_DebugExceptions;
    private bool m_DebugListening;

    // 動作確認の間に取り除きで消した Entity → 種類と prefab 名（宙に浮いた参照の内訳に「何を指していたか」を出す）。
    private readonly Dictionary<Entity, string> m_Removed = new();

    public override GameMode gameMode => GameMode.Game;

    protected override void OnCreate()
    {
        base.OnCreate();
        m_Camera = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
        m_SelectedInfo = World.GetOrCreateSystemManaged<SelectedInfoUISystem>();
        m_Simulation = World.GetOrCreateSystemManaged<SimulationSystem>();
        m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
        m_Prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
        m_Scanner = new DlcScanner(World, m_Prefabs, desc => GetEntityQuery(desc));
        // ゲームのデバッグ画面の「Force Send Renter Update Event」（DebugSystem）と同じ形のイベント。
        m_RentersUpdatedArchetype = EntityManager.CreateArchetype(ComponentType.ReadWrite<Game.Common.Event>(), ComponentType.ReadWrite<RentersUpdated>());
        // 持ち主を失いうる付属物（静的な物で、建物・施設の拡張・車両・人ではない）。
        m_OrphanQuery = GetEntityQuery(new EntityQueryDesc
        {
            All = new[]
            {
                ComponentType.ReadOnly<Owner>(),
                ComponentType.ReadOnly<Game.Objects.Object>(),
                ComponentType.ReadOnly<Game.Objects.Static>(),
            },
            None = new[]
            {
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<Game.Buildings.ServiceUpgrade>(),
                ComponentType.ReadOnly<Game.Vehicles.Vehicle>(),
                ComponentType.ReadOnly<Game.Creatures.Creature>(),
                ComponentType.ReadOnly<Temp>(),
                ComponentType.ReadOnly<Deleted>(),
            },
        });

        m_Binding = new ValueBinding<string>(Group, "dlc", Json());
        AddBinding(m_Binding);
        AddBinding(new TriggerBinding(Group, "scanDlc", Scan));
        AddBinding(new TriggerBinding<string, int>(Group, "jumpDlc", OnJump));
        AddBinding(new TriggerBinding<string>(Group, "removeDlc", OnRemove));

        m_CityConfiguration = World.GetOrCreateSystemManaged<CityConfigurationSystem>();
        m_UsedModsBinding = new ValueBinding<string>(Group, "usedMods", UsedModsJson());
        AddBinding(m_UsedModsBinding);
        AddBinding(new TriggerBinding(Group, "clearUsedMods", ClearUsedMods));
    }

    private string UsedModsJson() => UsedModsView.ToJson(m_CityConfiguration.usedMods, m_UsedModsCleared, m_UsedModsClearedCount);

    /// <summary>
    /// 「MOD 使用の記録」を消す（プレイヤーがボタンを押したときだけ）。ゲームは読み込みの最後にこの集合へ有効な mod の名前を足し、
    /// 保存で書き出す（SaveInfo の modsEnabled と実績の有効判定も見ている）。ECS の Entity ではないので、コマンドバッファは使わず直接消す。
    /// </summary>
    /// <remarks>消えるのは次の保存の印だけ。mod を有効にしたまま読み込み直すと、また足される。</remarks>
    private void ClearUsedMods()
    {
        if (!m_InGame)
        {
            return;
        }
        var set = m_CityConfiguration.usedMods;
        var count = set.Count;
        set.Clear();
        m_UsedModsCleared = true;
        m_UsedModsClearedCount += count;
        m_UsedModsBinding.Update(UsedModsJson());
        Debug.Log($"{Mod.LogPrefix} used mods cleared: {count} entries");
    }

    protected override void OnGamePreload(Purpose purpose, GameMode mode)
    {
        base.OnGamePreload(purpose, mode);
        m_InGame = false;
        // 別の街を読み込んだら前の一覧は無効（Entity が別物になる）。
        m_View = Array.Empty<DlcContentView>();
        m_Predicted = Array.Empty<string>();
        m_PredictedAfter = Array.Empty<string>();
        m_Last = null;
        m_State = "idle";
        m_RescanCountdown = 0;
        m_OrphansBefore = null;
        m_DebugEnabled = false;
        m_Removed.Clear();
        m_UsedModsCleared = false;
        m_UsedModsClearedCount = 0;
        m_DebugClearModsAt = 0f;
        StopListening();
        m_Binding.Update(Json());
        // 新しい街の記録は読み込みの最後（PostDeserialize）に作られる。ここでは消した印だけ戻して表示を更新し、読み込み完了で読み直す。
        m_UsedModsBinding.Update(UsedModsJson());
    }

    protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
    {
        base.OnGameLoadingComplete(purpose, mode);
        m_InGame = mode == GameMode.Game;
        m_DebugEnabled = m_InGame && DebugMarkers.Has("debug-dlc-remove");
        m_DebugStep = 0;
        // 診断（debug-dlc、読み込み完了の 5 秒後）の後に動かす。
        m_DebugAt = UnityEngine.Time.realtimeSinceStartup + 15f;
        // 記録は読み込みの最後（PostDeserialize）で足されるので、読み込み完了の後に読み直す。
        m_UsedModsBinding.Update(UsedModsJson());
        m_DebugClearModsAt = m_InGame && DebugMarkers.Has("debug-clear-mods") ? UnityEngine.Time.realtimeSinceStartup + 8f : 0f;
    }

    protected override void OnDestroy()
    {
        StopListening();
        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();
        if (!m_InGame)
        {
            return;
        }
        if (m_RescanCountdown > 0 && --m_RescanCountdown == 0)
        {
            if (m_OrphansBefore is not null)
            {
                // 取り除きの後始末: 新しく持ち主を失った付属物を片付け、その反映を待ってから探し直す（状態は removing のまま）。
                // 後始末が例外になっても探し直しへ進める（UI が「取り除いています」のまま止まらないように）。
                try
                {
                    CleanNewOrphans();
                }
                catch (Exception e)
                {
                    Debug.LogError($"{Mod.LogPrefix} dlc remove cleanup failed: {e}");
                    m_OrphansBefore = null;
                }
                m_RescanCountdown = OrphanRescanDelayFrames;
            }
            else
            {
                Scan();
            }
        }
        RunDebug();
        RunDebugClearMods();
    }

    /// <summary>動作確認用（debug-clear-mods）: 記録の内容をログに出し、消して、消した後の件数もログに出す（1 回だけ）。</summary>
    private void RunDebugClearMods()
    {
        if (m_DebugClearModsAt <= 0f || UnityEngine.Time.realtimeSinceStartup < m_DebugClearModsAt)
        {
            return;
        }
        m_DebugClearModsAt = 0f;
        Debug.Log($"{Mod.LogPrefix} used mods before: {UsedModsView.Describe(m_CityConfiguration.usedMods)}");
        ClearUsedMods();
        Debug.Log($"{Mod.LogPrefix} used mods after: {UsedModsView.Describe(m_CityConfiguration.usedMods)} json={UsedModsJson()}");
    }

    private string Json() => DlcView.ToJson(m_State, m_View, m_Predicted, m_PredictedAfter, m_Last);

    private void Scan()
    {
        var watch = Stopwatch.StartNew();
        var d = m_Scanner.Scan();
        m_View = d.BuildView();
        m_Predicted = d.PredictedContents().Select(c => c.Name).ToList();
        m_PredictedAfter = d.PredictedAfterRemoval().Select(c => c.Name).ToList();
        m_State = "ready";
        m_Binding.Update(Json());
        Debug.Log($"{Mod.LogPrefix} dlc scan: {Summary()} ({watch.ElapsedMilliseconds} ms)");
    }

    private void OnJump(string categoryKey, int index)
    {
        var category = m_View.SelectMany(c => c.Categories).FirstOrDefault(c => c.Key == categoryKey);
        if (category is null || category.Jumps.Count == 0)
        {
            return;
        }
        var j = category.Jumps[((index % category.Jumps.Count) + category.Jumps.Count) % category.Jumps.Count];
        var entity = DlcScanner.ToEntity(j.Holder);
        var em = EntityManager;
        var exists = em.Exists(entity) && !em.HasComponent<Deleted>(entity);
        // 車両は走り続けるので、今の位置を引き直す。消えていれば記録した位置へ。
        if (exists && EntityRules.HasPosition(em, entity))
        {
            var p = EntityRules.PositionOf(em, entity);
            CameraJump.To(m_Camera, p.x, p.y, p.z);
        }
        else if (j.HasPosition)
        {
            CameraJump.To(m_Camera, j.X, j.Y, j.Z);
        }
        // ゲームの選択状態にして、輪郭の強調と情報パネルを出す（路線は位置を持たないが、選べば路線の情報パネルが出る）。
        if (exists)
        {
            m_SelectedInfo.SetSelection(entity);
        }
    }

    /// <param name="contentKey">取り除くコンテンツのキー（prefab 名）。空なら欠落している全コンテンツ。</param>
    private void OnRemove(string contentKey)
    {
        var result = new DlcRemovalResult(contentKey);
        try
        {
            // 取り除きの前から持ち主の無い付属物を控える（後始末では、これ以外の「新しく」持ち主を失った物だけを片付ける）。
            m_OrphansBefore ??= CollectOrphans();
            Remove(contentKey, result);
        }
        catch (Exception e)
        {
            // 例外の前にコマンドバッファへ積んだ変更はフレームの終わりに反映されるので、件数はそこまでの分を残し、失敗の印を付ける。
            Debug.LogError($"{Mod.LogPrefix} dlc remove failed: {e}");
            result.Failed = true;
        }
        m_Last = result;
        m_State = "removing";
        m_Binding.Update(Json());
        // 失敗しても、後始末と探し直しは行う（途中までの変更が反映されるため）。
        m_RescanCountdown = RescanDelayFrames;
    }

    /// <summary>持ち主が Null か存在しない、静的な付属物（<see cref="m_OrphanQuery"/>）を集める。</summary>
    private HashSet<Entity> CollectOrphans()
    {
        var em = EntityManager;
        var result = new HashSet<Entity>();
        using var entities = m_OrphanQuery.ToEntityArray(Allocator.Temp);
        using var owners = m_OrphanQuery.ToComponentDataArray<Owner>(Allocator.Temp);
        for (var i = 0; i < entities.Length; i++)
        {
            var owner = owners[i].m_Owner;
            if (owner == Entity.Null || !em.Exists(owner))
            {
                result.Add(entities[i]);
            }
        }
        return result;
    }

    /// <summary>
    /// 取り除きの後に新しく持ち主を失った付属物（取り除きの前の控えに無いもの）とその子孫に、EndFrameBarrier で Deleted を付ける。
    /// </summary>
    /// <remarks>
    /// ゲームは持ち主に Deleted が付くと、SubObjectSystem が持ち主を「消える持ち主」として積み（CheckSubObjectOwnersJob :490, :559 の
    /// 削除フラグ）、UpdateSubObjectsJob が何も作らずに古い付属物を全部 Deleted にし、その子も DeepOwners で深さ順に消す
    /// （:1394 の RemoveUnusedOldSubObjects :1580-1621、:1395-1453）。持ち主を失った付属物は、その連鎖から漏れた物なので、Deleted を付ければ
    /// 通常の結果と同じになる。持ち主の SubObject バッファからの除去（SubObjectReferencesSystem :69）は TryGetBuffer で守られており、
    /// 持ち主が存在しなくても例外にならない。取り除きの前から持ち主の無い物（別の原因の取り残し）には触らない。
    /// </remarks>
    private void CleanNewOrphans()
    {
        var before = m_OrphansBefore;
        m_OrphansBefore = null;
        if (before is null)
        {
            return;
        }
        var em = EntityManager;
        if (m_DebugEnabled)
        {
            // 動作確認のときは、片付ける前の宙に浮いた参照の内訳（持ち主が何だったか: targetWas）を残す（取り残しの原因を調べるため）。
            Debug.Log($"{Mod.LogPrefix} dlc-remove before-cleanup frame={m_Simulation.frameIndex} {DanglingSummary()}");
        }
        var orphans = CollectOrphans();
        orphans.ExceptWith(before);
        var tree = new HashSet<Entity>(orphans);
        foreach (var e in orphans)
        {
            DlcScanner.CollectSubObjectTree(em, e, tree);
        }
        var buffer = m_EndFrameBarrier.CreateCommandBuffer();
        var cleaned = AddDeleted(buffer, tree.ToList());
        if (m_Last is not null)
        {
            m_Last.OrphansCleaned += cleaned;
        }
        if (m_DebugEnabled)
        {
            RecordRemoved(tree, "orphan");
        }
        Debug.Log($"{Mod.LogPrefix} dlc remove cleanup: orphansCleaned={cleaned} (newly ownerless={orphans.Count}, "
            + $"descendants={tree.Count - orphans.Count}, ownerless before removal (untouched)={before.Count}) {OrphanBreakdown(orphans)}");
    }

    /// <summary>片付けた付属物の prefab 名ごとの件数（多い順に 8 つ）。原因を後で調べるためのログ用。</summary>
    private string OrphanBreakdown(IEnumerable<Entity> orphans)
    {
        var em = EntityManager;
        var groups = orphans
            .GroupBy(e => em.HasComponent<PrefabRef>(e) ? em.GetComponentData<PrefabRef>(e).m_Prefab : Entity.Null)
            .Select(g => (Name: g.Key == Entity.Null ? "(no PrefabRef)" : m_Prefabs.GetPrefabName(g.Key), Count: g.Count()))
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Name, StringComparer.Ordinal)
            .Take(8)
            .Select(g => $"'{g.Name}' x{g.Count}")
            .ToList();
        return groups.Count == 0 ? string.Empty : "kinds: " + string.Join(", ", groups);
    }

    /// <param name="result">結果。例外で中断しても、それまでの件数が残るよう呼び出し側が持つ。</param>
    private void Remove(string contentKey, DlcRemovalResult result)
    {
        var watch = Stopwatch.StartNew();
        var em = EntityManager;

        // ボタンを押した時点の街で照会し直す（一覧を作った後に車両は入れ替わっている）。
        var d = m_Scanner.Scan();
        var keys = contentKey.Length > 0
            ? new[] { contentKey }
            : d.BuildView().Where(v => v.Status != DlcContentStatus.Loaded).Select(v => v.Key).ToArray();
        var targets = new HashSet<Entity>();
        foreach (var key in keys)
        {
            foreach (var id in d.RemovalTargets(key))
            {
                targets.Add(DlcScanner.ToEntity(id));
            }
        }
        if (targets.Count == 0)
        {
            Debug.Log($"{Mod.LogPrefix} dlc remove '{contentKey}': nothing to remove");
            return;
        }
        // 記録の直前に、参照先がまだ欠落しているかを確かめ直す（取り除く対象は欠落している prefab だけ）。
        bool IsTarget(Entity prefab) => targets.Contains(prefab) && DlcScanner.IsMissing(em, prefab);

        var buffer = m_EndFrameBarrier.CreateCommandBuffer();

        // 1. 企業のブランドの付け替え。企業 prefab そのものが欠落した企業は付け替えず、5 で企業ごと転出させる。
        var forbidden = DlcScanner.ForbiddenContents(d);
        var rebrandedProperties = new HashSet<Entity>();
        var movingAway = new HashSet<Entity>();
        using (var companies = m_Scanner.CompanyQuery.ToEntityArray(Allocator.Temp))
        using (var data = m_Scanner.CompanyQuery.ToComponentDataArray<CompanyData>(Allocator.Temp))
        {
            for (var i = 0; i < companies.Length; i++)
            {
                var company = companies[i];
                var c = data[i];
                if (!IsTarget(c.m_Brand))
                {
                    continue;
                }
                if (!em.HasComponent<PrefabRef>(company))
                {
                    result.Skipped++;
                    continue;
                }
                var companyPrefab = em.GetComponentData<PrefabRef>(company).m_Prefab;
                if (DlcScanner.IsMissing(em, companyPrefab))
                {
                    movingAway.Add(company);
                    continue;
                }
                var candidates = m_Scanner.BrandCandidates(d, companyPrefab);
                var chosen = DlcRemoval.SelectBrand(candidates, forbidden, c.m_RandomSeed.state);
                if (chosen < 0)
                {
                    // 付け替え先が無ければ Entity.Null にはせず残す（Null は名前表示が壊れる: NameSystem）。
                    result.Skipped++;
                    continue;
                }
                c.m_Brand = DlcScanner.ToEntity(candidates[chosen].Brand);
                buffer.SetComponent(company, c);
                result.Rebranded++;
                if (em.HasComponent<PropertyRenter>(company))
                {
                    var property = em.GetComponentData<PropertyRenter>(company).m_Property;
                    if (property != Entity.Null && em.Exists(property))
                    {
                        rebrandedProperties.Add(property);
                    }
                }
            }
        }
        // 物件の看板を新しいブランドで組み直させる（PropertyProcessingSystem が入居時に送るのと同じイベント）。
        // 同じ物件へ重ねて送らないよう、送った物件を控える。
        var rentersUpdated = new HashSet<Entity>();
        foreach (var property in rebrandedProperties)
        {
            SendRentersUpdatedOnce(buffer, rentersUpdated, property);
        }

        // 2〜4 の対象を集める。
        var subObjects = new List<Entity>();
        // 付属物 → 組み直しを頼む最上位の持ち主（無ければ Entity.Null）。
        var subObjectTops = new Dictionary<Entity, Entity>();
        var subObjectsOwnerMissing = 0;
        var deletes = new List<Entity>();
        var vehicles = new HashSet<Entity>();
        using (var entities = m_Scanner.PrefabRefQuery.ToEntityArray(Allocator.Temp))
        using (var refs = m_Scanner.PrefabRefQuery.ToComponentDataArray<PrefabRef>(Allocator.Temp))
        {
            for (var i = 0; i < entities.Length; i++)
            {
                if (!targets.Contains(refs[i].m_Prefab))
                {
                    continue;
                }
                var e = entities[i];
                if (!IsTarget(refs[i].m_Prefab))
                {
                    result.Skipped++;
                    continue;
                }
                var kind = DlcScanner.HolderKindOf(em, e);
                switch (kind)
                {
                    case DlcHolderKind.Vehicle:
                        // 連結車両は先頭（Controller）で重複を除き、編成ごと消す。
                        var leader = e;
                        if (em.HasComponent<Controller>(e))
                        {
                            var controller = em.GetComponentData<Controller>(e).m_Controller;
                            if (controller != Entity.Null && em.Exists(controller))
                            {
                                leader = controller;
                            }
                        }
                        vehicles.Add(leader);
                        break;
                    case DlcHolderKind.SubObject:
                        if (DlcScanner.OwnerWillBeRemoved(em, e, IsTarget))
                        {
                            // 持ち主と一緒に消える。
                            break;
                        }
                        // 走査と同じ判定（DlcScanner.CanRebuildOwner）。最上位の持ち主の prefab が欠落していて消されないなら触らない。
                        if (!DlcScanner.CanRebuildOwner(em, e, out var top))
                        {
                            subObjectsOwnerMissing++;
                            result.Skipped++;
                            break;
                        }
                        subObjects.Add(e);
                        subObjectTops[e] = top;
                        break;
                    case DlcHolderKind.Building:
                    case DlcHolderKind.PlacedObject:
                        if (DlcScanner.IsDeletable(em, e, kind))
                        {
                            deletes.Add(e);
                        }
                        else
                        {
                            result.Skipped++;
                        }
                        break;
                    case DlcHolderKind.Company:
                        movingAway.Add(e);
                        break;
                    default:
                        // 道路・エリア・人などは一覧だけ（探し直した一覧に残りとして出る）。
                        break;
                }
            }
        }

        // 2. 付属物を消し、最上位の持ち主に組み直させる（ゲームが入れ子を最上位の文脈で組み直すのに合わせる。DlcScanner.TopOwner）。
        var owners = new HashSet<Entity>();
        // 直接の持ち主が中間の付属物だったもの（中間 → 最上位）。以前は直接の持ち主へ送っていたので、その影響を実機のログで調べるために数える。
        var intermediates = new Dictionary<Entity, Entity>();
        var directOwners = new HashSet<Entity>();
        foreach (var e in subObjects)
        {
            var top = subObjectTops[e];
            if (top != Entity.Null)
            {
                owners.Add(top);
            }
            var direct = em.GetComponentData<Owner>(e).m_Owner;
            if (direct != Entity.Null && em.Exists(direct))
            {
                directOwners.Add(direct);
                if (direct != top)
                {
                    intermediates[direct] = top;
                }
            }
        }
        // 付属物の子孫も一緒に消す（宙に浮いた Owner が残った件の保険。ゲームの連鎖と結果は同じ。DlcScanner.CollectSubObjectTree）。
        var subObjectTree = new HashSet<Entity>(subObjects);
        var children = 0;
        foreach (var e in subObjects)
        {
            children += DlcScanner.CollectSubObjectTree(em, e, subObjectTree);
        }
        result.Deleted += AddDeleted(buffer, subObjectTree.ToList());
        foreach (var owner in owners)
        {
            SendRentersUpdatedOnce(buffer, rentersUpdated, owner);
        }

        // 3. 建物・置き物。
        result.Deleted += AddDeleted(buffer, deletes);

        // 4. 車両（編成ごと）。
        foreach (var vehicle in vehicles)
        {
            var layout = em.HasBuffer<LayoutElement>(vehicle) ? em.GetBuffer<LayoutElement>(vehicle, isReadOnly: true) : default;
            VehicleUtils.DeleteVehicle(buffer, vehicle, layout);
            result.Deleted += layout.IsCreated && layout.Length != 0 ? layout.Length : 1;
        }

        // 5. 企業 prefab が欠落した企業を転出させる（Game.Simulation.CompanyMoveAwaySystem の MovingAwayJob と同じ手順）。
        var movedProperties = new HashSet<Entity>();
        result.MovedAway = MoveAway(buffer, movingAway, rentersUpdated, movedProperties);

        // 調査用: 以前の送り方（直接の持ち主へ送る）なら、中間の付属物とその最上位が同じフレームに両方組み直されていた数。
        // 上の持ち主（最上位まで）のどれかが、ほかの理由（別の付属物の直接の持ち主、ブランドの付け替え・転出の物件）でも
        // 送られていた中間の数を数える。
        var oldTargets = new HashSet<Entity>(directOwners);
        oldTargets.UnionWith(rebrandedProperties);
        oldTargets.UnionWith(movedProperties);
        bool AncestorAlsoUpdated(Entity intermediate)
        {
            var a = intermediate;
            for (var depth = 0; depth < 8 && em.HasComponent<Owner>(a); depth++)
            {
                a = em.GetComponentData<Owner>(a).m_Owner;
                if (a == Entity.Null || !em.Exists(a))
                {
                    return false;
                }
                if (oldTargets.Contains(a))
                {
                    return true;
                }
            }
            return false;
        }
        var intermediatesWithAncestorAlsoUpdated = intermediates.Keys.Count(AncestorAlsoUpdated);

        if (m_DebugEnabled)
        {
            // 動作確認のときだけ、消した物の種類と prefab 名を控える（宙に浮いた参照が何を指していたかを内訳に出すため）。
            RecordRemoved(subObjects, "subObject");
            RecordRemoved(subObjectTree.Except(subObjects), "subObjectChild");
            RecordRemoved(deletes, "buildingOrObject");
            foreach (var vehicle in vehicles)
            {
                var layout = em.HasBuffer<LayoutElement>(vehicle) ? em.GetBuffer<LayoutElement>(vehicle, isReadOnly: true) : default;
                if (layout.IsCreated && layout.Length != 0)
                {
                    var cars = new List<Entity>(layout.Length);
                    for (var i = 0; i < layout.Length; i++)
                    {
                        cars.Add(layout[i].m_Vehicle);
                    }
                    RecordRemoved(cars, "vehicle");
                }
                else
                {
                    RecordRemoved(new[] { vehicle }, "vehicle");
                }
            }
            RecordRemoved(movingAway, "company");
        }

        // 6. 路線の車両指定。車両を消したのと同じ操作で外す（残すと、ゲームが欠落した型式の車両を求め続ける）。
        using (var routes = m_Scanner.VehicleModelQuery.ToEntityArray(Allocator.Temp))
        {
            foreach (var route in routes)
            {
                if (!em.HasBuffer<VehicleModel>(route))
                {
                    continue;
                }
                var models = em.GetBuffer<VehicleModel>(route, isReadOnly: true);
                var before = new List<(EntityId, EntityId)>(models.Length);
                for (var i = 0; i < models.Length; i++)
                {
                    before.Add((DlcScanner.ToId(models[i].m_PrimaryPrefab), DlcScanner.ToId(models[i].m_SecondaryPrefab)));
                }
                var after = DlcRemoval.RewriteVehicleModels(before, id => IsTarget(DlcScanner.ToEntity(id)), out var cleared);
                if (cleared == 0)
                {
                    continue;
                }
                // ゲームの SelectVehiclesSection と同じく、メインスレッドで EntityManager から書き換える。
                var writable = em.GetBuffer<VehicleModel>(route, isReadOnly: false);
                writable.Clear();
                foreach (var (primary, secondary) in after)
                {
                    writable.Add(new VehicleModel { m_PrimaryPrefab = DlcScanner.ToEntity(primary), m_SecondaryPrefab = DlcScanner.ToEntity(secondary) });
                }
                result.RoutesCleared++;
            }
        }

        Debug.Log($"{Mod.LogPrefix} dlc remove '{(contentKey.Length > 0 ? contentKey : "(all missing)")}': targets={targets.Count} "
            + $"subObjects={subObjects.Count} subObjectChildren={children} subObjectsOwnerMissing={subObjectsOwnerMissing} owners={owners.Count} "
            + $"directOwners={directOwners.Count} intermediateOwners={intermediates.Count} intermediatesWithAncestorAlsoUpdated={intermediatesWithAncestorAlsoUpdated} "
            + $"buildingsObjects={deletes.Count} vehicleTrains={vehicles.Count} "
            + $"deleted={result.Deleted} rebranded={result.Rebranded} properties={rebrandedProperties.Count} movedAway={result.MovedAway} "
            + $"rentersUpdated={rentersUpdated.Count} routesCleared={result.RoutesCleared} "
            + $"skipped={result.Skipped} ({watch.ElapsedMilliseconds} ms)");
    }

    private static int AddDeleted(EntityCommandBuffer buffer, List<Entity> entities)
    {
        if (entities.Count == 0)
        {
            return 0;
        }
        // 一括版で積む（コマンドバッファは配列を写し取るので、Temp の配列はこの場で破棄してよい）。
        var array = new NativeArray<Entity>(entities.ToArray(), Allocator.Temp);
        try
        {
            buffer.AddComponent<Deleted>(array);
        }
        finally
        {
            array.Dispose();
        }
        return entities.Count;
    }

    /// <summary>
    /// 企業を転出させる。Game.Simulation.CompanyMoveAwaySystem の MovingAwayJob と同じく、物件が市場に出ていず放棄もされていなければ
    /// PropertyToBeOnMarket を付け（PropertyProcessingSystem が空き物件として市場へ出す）、物件へ RentersUpdated を送り、企業に Deleted を付ける。
    /// </summary>
    /// <remarks>
    /// MovingAway を付けてゲームに任せる方法は使わない。転出の処理は約 1024 フレームごとにしか動かず（時間が止まっていれば動かない）、
    /// 転出の実行の照会（m_MovingAwayQuery）が ProcessingCompany と PropertyRenter を必須にするため。抽出企業（ExtractorCompany）を
    /// 除いているのは転出の判定側の照会（m_CompanyQuery、CompanyMoveAwaySystem :497-509）だけで、実行側（:510-517）は除かない。
    /// Deleted の企業の後始末はゲームが行う（MovingAwayJob の後と同じ）: Game.Simulation.RemovedSystem が従業員の Worker・
    /// 通知アイコン（CompanyNotifications・WorkProvider）・物件の Renter バッファを片付け、Game.Objects.SubElementDeleteSystem が
    /// 持っている車両（OwnedVehicle）を消す。資源・取引の記録は企業の Entity と一緒に消える。物件（建物）は消さない。
    /// </remarks>
    /// <returns>転出させた企業の数。</returns>
    /// <param name="properties">RentersUpdated を送った物件を足す（調査用のログに使う）。</param>
    private int MoveAway(EntityCommandBuffer buffer, HashSet<Entity> companies, HashSet<Entity> rentersUpdated, HashSet<Entity> properties)
    {
        var em = EntityManager;
        foreach (var company in companies)
        {
            if (!em.HasComponent<PropertyRenter>(company))
            {
                continue;
            }
            var property = em.GetComponentData<PropertyRenter>(company).m_Property;
            if (property == Entity.Null || !em.Exists(property))
            {
                continue;
            }
            if (!em.HasComponent<PropertyOnMarket>(property) && !em.HasComponent<Abandoned>(property))
            {
                buffer.AddComponent<PropertyToBeOnMarket>(property);
            }
            SendRentersUpdatedOnce(buffer, rentersUpdated, property);
            properties.Add(property);
        }
        return AddDeleted(buffer, companies.ToList());
    }

    /// <summary>動作確認用に、消した Entity の種類と prefab 名を控える（m_Removed）。</summary>
    private void RecordRemoved(IEnumerable<Entity> entities, string kind)
    {
        var em = EntityManager;
        var names = new Dictionary<Entity, string>();
        foreach (var e in entities)
        {
            var prefab = em.HasComponent<PrefabRef>(e) ? em.GetComponentData<PrefabRef>(e).m_Prefab : Entity.Null;
            if (!names.TryGetValue(prefab, out var name))
            {
                name = prefab == Entity.Null ? "(no PrefabRef)" : m_Prefabs.GetPrefabName(prefab);
                names[prefab] = name;
            }
            m_Removed[e] = $"{kind} '{name}'";
        }
    }

    /// <summary>物件へ RentersUpdated を送る（同じ取り除きの中で同じ物件へは 1 回だけ）。</summary>
    private void SendRentersUpdatedOnce(EntityCommandBuffer buffer, HashSet<Entity> sent, Entity property)
    {
        if (!sent.Add(property))
        {
            return;
        }
        var e = buffer.CreateEntity(m_RentersUpdatedArchetype);
        buffer.SetComponent(e, new RentersUpdated(property));
    }

    private string Summary() => m_View.Count == 0
        ? "(none)"
        : string.Join("; ", m_View.Select(c =>
            $"{c.Key}[{DlcView.StatusKey(c.Status)}] removable={c.Removable} blocked={c.Blocked} remainsAfter={c.RemainsAfter} "
            + string.Join(" ", c.Categories.Select(g => $"{DlcRemoval.CategoryKey(g.Category)}/{DlcRemoval.ActionKey(g.Action)}={g.Count}"))))
        + $" | predicted={string.Join(",", m_Predicted)} predictedAfter={string.Join(",", m_PredictedAfter)}";

    /// <summary>
    /// 動作確認用（DebugMarkers の debug-dlc-remove）の自動操作。パネルを DLC タブで開き、一覧の画面を保存し、
    /// 欠落したコンテンツを取り除き、直後と時間を進めた後に探し直して、宙に浮いた参照の数と一緒に Player.log へ書く。
    /// </summary>
    private void RunDebug()
    {
        if (!m_DebugEnabled || m_DebugStep == 99)
        {
            return;
        }
        var now = UnityEngine.Time.realtimeSinceStartup;
        if (now < m_DebugAt)
        {
            return;
        }
        try
        {
            RunDebugStep(now);
        }
        catch (Exception e)
        {
            Debug.LogError($"{Mod.LogPrefix} dlc-remove debug exception at step {m_DebugStep}: {e}");
            m_DebugStep = 99;
            StopListening();
        }
    }

    private void OnLogMessage(string condition, string stackTrace, UnityEngine.LogType type)
    {
        if (type == UnityEngine.LogType.Exception)
        {
            m_DebugExceptions++;
        }
    }

    private void StopListening()
    {
        if (m_DebugListening)
        {
            UnityEngine.Application.logMessageReceived -= OnLogMessage;
            m_DebugListening = false;
        }
    }

    private void RunDebugStep(float now)
    {
        switch (m_DebugStep)
        {
            case 0:
                if (!m_DebugListening)
                {
                    UnityEngine.Application.logMessageReceived += OnLogMessage;
                    m_DebugListening = true;
                }
                m_DebugExceptions = 0;
                Scan();
                Debug.Log($"{Mod.LogPrefix} dlc-remove before frame={m_Simulation.frameIndex} {DanglingSummary()}");
                // 一覧が表示されてから画面を保存する。
                m_DebugAt = now + 3f;
                m_DebugStep = 1;
                break;
            case 1:
                UnityEngine.ScreenCapture.CaptureScreenshot(DebugMarkers.PathOf("debug-dlc-list.png"));
                m_DebugAt = now + 2f;
                m_DebugStep = 2;
                break;
            case 2:
            {
                // 目印ファイルの中身にコンテンツ名（1 行に 1 つ）があれば、それだけを取り除く。空なら欠落している全コンテンツ。
                var names = File.ReadAllLines(DebugMarkers.PathOf("debug-dlc-remove"))
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0)
                    .ToList();
                m_DebugFrame = m_Simulation.frameIndex;
                if (names.Count == 0)
                {
                    OnRemove(string.Empty);
                    Debug.Log($"{Mod.LogPrefix} dlc-remove requested (all missing) frame={m_DebugFrame}");
                }
                foreach (var name in names)
                {
                    OnRemove(name);
                    Debug.Log($"{Mod.LogPrefix} dlc-remove requested '{name}' frame={m_DebugFrame}");
                }
                m_DebugStep = 3;
                break;
            }
            case 3:
                // 取り除きの 10 フレーム後の探し直しが済むのを待つ。
                if (m_RescanCountdown > 0 || m_State != "ready")
                {
                    return;
                }
                Debug.Log($"{Mod.LogPrefix} dlc-remove after-rescan frame={m_Simulation.frameIndex} (+{m_Simulation.frameIndex - m_DebugFrame}) exceptions={m_DebugExceptions} {Summary()} {DanglingSummary()}");
                // 探し直した一覧が UI に描かれてから画面を保存する。
                m_DebugAt = now + 3f;
                m_DebugStep = 5;
                break;
            case 5:
                UnityEngine.ScreenCapture.CaptureScreenshot(DebugMarkers.PathOf("debug-dlc-removed.png"));
                // 乗車中の住民などの後始末はシミュレーションの進行で走るので、時間を進める（止まっていれば動かす）。
                if (DebugMarkers.Has("debug-speed3"))
                {
                    m_Simulation.selectedSpeed = 3f;
                }
                else if (m_Simulation.selectedSpeed <= 0f)
                {
                    m_Simulation.selectedSpeed = 1f;
                }
                Debug.Log($"{Mod.LogPrefix} dlc-remove advancing speed={m_Simulation.selectedSpeed}");
                m_DebugFrame = m_Simulation.frameIndex;
                m_DebugAt = now + 60f;
                m_DebugStep = 4;
                break;
            case 4:
                Scan();
                Debug.Log($"{Mod.LogPrefix} dlc-remove after-advance frame={m_Simulation.frameIndex} (+{m_Simulation.frameIndex - m_DebugFrame}) exceptions={m_DebugExceptions} {Summary()} {DanglingSummary()}");
                m_DebugAt = now + 3f;
                m_DebugStep = 6;
                break;
            case 6:
                UnityEngine.ScreenCapture.CaptureScreenshot(DebugMarkers.PathOf("debug-dlc-after.png"));
                Debug.Log($"{Mod.LogPrefix} dlc-remove debug done exceptions={m_DebugExceptions}");
                StopListening();
                m_DebugStep = 99;
                break;
        }
    }

    /// <summary>
    /// 参照先が存在しない（宙に浮いた）参照を数えて集計の 1 行を返し、内訳は別の行として Player.log へ書く（DanglingReferences）。
    /// </summary>
    private string DanglingSummary()
    {
        var lines = DanglingReferences.Describe(EntityManager, m_Prefabs, m_Scanner.CompanyQuery, m_Removed.Count > 0 ? m_Removed : null);
        foreach (var line in lines.Skip(1))
        {
            Debug.Log($"{Mod.LogPrefix} dlc-remove {line}");
        }
        return lines[0];
    }
}
