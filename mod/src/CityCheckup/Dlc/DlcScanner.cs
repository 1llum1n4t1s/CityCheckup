using System;
using System.Collections.Generic;
using Game.Areas;
using Game.Buildings;
using Game.Common;
using Game.Companies;
using Game.Effects;
using Game.Net;
using Game.Policies;
using Game.Prefabs;
using Game.Routes;
using Game.Simulation;
using Game.Tools;
using CityCheckup.Common;
using CityCheckup.Core;
using Unity.Collections;
using Unity.Entities;

namespace CityCheckup.Dlc;

/// <summary>
/// セーブが DLC などのコンテンツを要求する原因になっている参照を照会し、Core の <see cref="DlcDiagnostics"/> に分類つきで数えさせる。
/// 診断（DlcDiagnosticSystem）・DLC タブ・取り除き（DlcRemovalSystem）で共有する。読むだけで、何も書き換えない。
/// </summary>
/// <remarks>
/// 照会の条件は保存時の PrimaryPrefabReferencesSystem.OnCreate（:537-588）と同じにしてある。条件を変えると
/// 「次の保存で要求される」予測がセーブのメタデータとずれるので、変えるときは実機の診断で一致を確かめ直すこと。
/// システムが持つ参照（PrimaryPrefabReferencesSystem.PatchSystemReferences :727-740）も、公開されているものは読む
/// （<see cref="ScanSystemReferences"/>）。ClimateSystem の天気の効果（m_CurrentWeatherEffects / m_NextWeatherEffects）は
/// 非公開で読めないため数えない（今の気候 prefab の既定の天気から選ばれる: ClimateSystem.SelectWeatherPlaceholder）。
/// Secondary 側は ContentPrerequisiteData / ModPrerequisiteData の伝播だけを再現する（Core の DlcDiagnostics）。
/// </remarks>
internal sealed class DlcScanner
{
    // 持ち主の入れ子の段数の上限（ゲームの付属物の入れ子は depth < 7 まで: SubObjectSystem.CreateSubObjects）。
    private const int MaxOwnerDepth = 8;

    private readonly EntityManager m_EntityManager;
    private readonly PrefabSystem m_Prefabs;
    private readonly World m_World;

    private readonly EntityQuery m_ContentQuery;
    private readonly EntityQuery m_ContentPrerequisiteQuery;
    private readonly EntityQuery m_ModPrerequisiteQuery;
    private readonly EntityQuery m_UnderConstructionQuery;
    private readonly EntityQuery m_PolicyQuery;
    private readonly EntityQuery m_ServiceBudgetQuery;
    private readonly EntityQuery m_EditorContainerQuery;
    private readonly EntityQuery m_ChirpQuery;
    private readonly EntityQuery m_SubReplacementQuery;

    // 走査の間だけ使う、prefab ごとの判定の控え（同じ prefab を何万回も引かないため）。
    private readonly Dictionary<Entity, bool> m_MissingCache = new();
    private readonly Dictionary<Entity, bool> m_BrandCandidateCache = new();

    /// <param name="world">システムが持つ参照（街の設定・統計の記録）を読むためのワールド。</param>
    /// <param name="getQuery">システムの GetEntityQuery（照会はシステムが持ち、破棄もシステムに任せる）。</param>
    public DlcScanner(World world, PrefabSystem prefabs, Func<EntityQueryDesc, EntityQuery> getQuery)
    {
        m_World = world;
        m_EntityManager = world.EntityManager;
        m_Prefabs = prefabs;

        // prefab 側の照会。欠落した prefab は PrefabData が無効なので、有効・無効を問わず拾う。
        const EntityQueryOptions prefabOptions =
            EntityQueryOptions.IgnoreComponentEnabledState | EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities;
        m_ContentQuery = getQuery(PrefabDesc<ContentData>(prefabOptions));
        m_ContentPrerequisiteQuery = getQuery(PrefabDesc<ContentPrerequisiteData>(prefabOptions));
        m_ModPrerequisiteQuery = getQuery(PrefabDesc<ModPrerequisiteData>(prefabOptions));

        // 街側の照会。PrimaryPrefabReferencesSystem.OnCreate（:537-588）と同じ条件。
        PrefabRefQuery = getQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<PrefabRef>() },
            None = new[]
            {
                ComponentType.ReadOnly<NetCompositionData>(),
                ComponentType.ReadOnly<EffectInstance>(),
                ComponentType.ReadOnly<LivePath>(),
                ComponentType.ReadOnly<Temp>(),
                ComponentType.ReadOnly<Deleted>(),
            },
        });
        m_UnderConstructionQuery = getQuery(LiveDesc<Game.Objects.UnderConstruction>());
        CompanyQuery = getQuery(LiveDesc<CompanyData>());
        m_PolicyQuery = getQuery(LiveDesc<Policy>());
        // ServiceBudgetData は除外条件なし。
        m_ServiceBudgetQuery = getQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<ServiceBudgetData>() } });
        VehicleModelQuery = getQuery(LiveDesc<VehicleModel>());
        m_EditorContainerQuery = getQuery(LiveDesc<Game.Tools.EditorContainer>());
        m_ChirpQuery = getQuery(LiveDesc<Game.Triggers.Chirp>());
        m_SubReplacementQuery = getQuery(LiveDesc<SubReplacement>());
    }

    /// <summary>PrefabRef を持つ街の Entity（保存時の照会と同じ条件）。</summary>
    public EntityQuery PrefabRefQuery { get; }

    /// <summary>企業（CompanyData を持つ Entity）。</summary>
    public EntityQuery CompanyQuery { get; }

    /// <summary>路線（VehicleModel バッファを持つ Entity）。</summary>
    public EntityQuery VehicleModelQuery { get; }

    private static EntityQueryDesc PrefabDesc<T>(EntityQueryOptions options) => new()
    {
        All = new[] { ComponentType.ReadOnly<T>() },
        None = new[] { ComponentType.ReadOnly<Deleted>() },
        Options = options,
    };

    private static EntityQueryDesc LiveDesc<T>() => new()
    {
        All = new[] { ComponentType.ReadOnly<T>() },
        None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
    };

    /// <summary>街の参照を全経路でたどり、分類つきで数える。数百 ms かかるので、操作や目印があるときだけ呼ぶ。</summary>
    public DlcDiagnostics Scan()
    {
        m_MissingCache.Clear();
        m_BrandCandidateCache.Clear();
        var em = m_EntityManager;
        var d = new DlcDiagnostics(ReadContents(em), ReadPrerequisites(em));

        Scan<PrefabRef>(d, PrefabRefQuery, DlcRefPath.PrefabRef, r => r.m_Prefab);
        Scan<Game.Objects.UnderConstruction>(d, m_UnderConstructionQuery, DlcRefPath.UnderConstruction, u => u.m_NewPrefab);
        Scan<CompanyData>(d, CompanyQuery, DlcRefPath.CompanyBrand, c => c.m_Brand);
        ScanBuffer<Policy>(d, m_PolicyQuery, DlcRefPath.Policy, p => p.m_Policy);
        ScanBuffer<ServiceBudgetData>(d, m_ServiceBudgetQuery, DlcRefPath.ServiceBudget, b => b.m_Service);
        ScanBuffer<VehicleModel>(d, VehicleModelQuery, DlcRefPath.VehicleModelPrimary, v => v.m_PrimaryPrefab);
        ScanBuffer<VehicleModel>(d, VehicleModelQuery, DlcRefPath.VehicleModelSecondary, v => v.m_SecondaryPrefab);
        Scan<Game.Tools.EditorContainer>(d, m_EditorContainerQuery, DlcRefPath.EditorContainer, c => c.m_Prefab);
        // Chirp は参照先が PrefabData を持つ場合だけ印が付く（FixChirpJob）。
        Scan<Game.Triggers.Chirp>(d, m_ChirpQuery, DlcRefPath.ChirpSender, c => c.m_Sender, requirePrefabData: true);
        ScanBuffer<Game.Triggers.ChirpEntity>(d, m_ChirpQuery, DlcRefPath.ChirpEntity, c => c.m_Entity, requirePrefabData: true);
        ScanBuffer<SubReplacement>(d, m_SubReplacementQuery, DlcRefPath.SubReplacement, s => s.m_Prefab);
        ScanSystemReferences(d);
        return d;
    }

    /// <summary>
    /// システムが持つ参照を数える（PrimaryPrefabReferencesSystem.PatchSystemReferences と同じ相手。どれも一覧だけ）。
    /// </summary>
    /// <remarks>
    /// 保存時は Entity の参照と同じ PrefabReferences.Check で印が付き、1 回目の CheckPrefabReferencesSystem より前なので
    /// ContentPrerequisiteData の伝播にも乗る（Game.Common.SystemOrder :727-730）。
    /// 建物ごとの輸送の記録のキーは、輸送の実績による解除条件（Game.Prefabs.TransportRequirementSystem :101）が読むので消さない。
    /// </remarks>
    private void ScanSystemReferences(DlcDiagnostics d)
    {
        var city = m_World.GetExistingSystemManaged<Game.City.CityConfigurationSystem>();
        if (city is not null)
        {
            VisitSystem(d, DlcRefPath.CityTheme, city.defaultTheme, "CityConfigurationSystem.defaultTheme");
            VisitSystem(d, DlcRefPath.CityTheme, city.loadedDefaultTheme, "CityConfigurationSystem.loadedDefaultTheme");
            ref var required = ref city.requiredContent;
            if (required.IsCreated)
            {
                for (var i = 0; i < required.Length; i++)
                {
                    VisitSystem(d, DlcRefPath.CityRequiredContent, required[i], "CityConfigurationSystem.requiredContent");
                }
            }
        }
        var climate = m_World.GetExistingSystemManaged<ClimateSystem>();
        if (climate is not null)
        {
            VisitSystem(d, DlcRefPath.Climate, climate.currentClimate, "ClimateSystem.currentClimate");
        }
        var terrain = m_World.GetExistingSystemManaged<Game.Rendering.TerrainMaterialSystem>();
        if (terrain is not null)
        {
            VisitSystem(d, DlcRefPath.TerrainRenderSettings, terrain.currentTerrainRenderSettings, "TerrainMaterialSystem.currentTerrainRenderSettings");
        }
        var water = m_World.GetExistingSystemManaged<Game.Rendering.WaterRenderSystem>();
        if (water is not null)
        {
            VisitSystem(d, DlcRefPath.WaterRenderSettings, water.currentWaterRenderSettings, "WaterRenderSystem.currentWaterRenderSettings");
        }
        var transport = m_World.GetExistingSystemManaged<TransportUsageTrackSystem>();
        if (transport is not null)
        {
            // 書き込みのジョブを待ってからメインスレッドで読む（キーを読むだけで、記録は変えない）。
            var usage = transport.GetBuildingPrefabTransportUsageData(out var dependencies);
            dependencies.Complete();
            if (usage.IsCreated)
            {
                using var keys = usage.GetKeyArray(Allocator.Temp);
                foreach (var building in keys)
                {
                    VisitSystem(d, DlcRefPath.TransportUsage, building, "TransportUsageTrackSystem (building prefab)");
                }
            }
        }
    }

    /// <summary>システムが持つ参照 1 件を数える。保有 Entity が無いので「見に行く」先は足さない（診断ログの見本だけ）。</summary>
    private void VisitSystem(DlcDiagnostics d, DlcRefPath path, Entity target, string holderName)
    {
        if (target == Entity.Null)
        {
            return;
        }
        var targetId = ToId(target);
        if (!d.IsRelevant(targetId))
        {
            return;
        }
        var cls = DlcRemoval.Classify(path, DlcHolderKind.Other, MissingCached(target), canAct: false);
        if (d.Record(path, targetId, cls))
        {
            d.AddSample(path, targetId, new DlcReferenceSample(default, holderName, false, 0f, 0f, 0f));
        }
    }

    /// <summary>欠落した prefab か。PrefabData が無い・無効、または obsolete（m_Index が負）の prefab。</summary>
    public static bool IsMissing(EntityManager em, Entity prefab)
    {
        if (!em.Exists(prefab) || !em.HasComponent<PrefabData>(prefab))
        {
            return true;
        }
        return !em.IsComponentEnabled<PrefabData>(prefab) || em.GetComponentData<PrefabData>(prefab).m_Index < 0;
    }

    /// <summary>PrefabRef を持つ Entity の種類。判定の順序に意味がある（車両や建物も Owner を持つことがあるので、付属物より先に見る）。</summary>
    public static DlcHolderKind HolderKindOf(EntityManager em, Entity e)
    {
        if (em.HasComponent<Game.Vehicles.Vehicle>(e))
        {
            return DlcHolderKind.Vehicle;
        }
        if (em.HasComponent<Game.Creatures.Creature>(e))
        {
            return DlcHolderKind.Creature;
        }
        if (em.HasComponent<Building>(e))
        {
            return DlcHolderKind.Building;
        }
        if (em.HasComponent<CompanyData>(e))
        {
            return DlcHolderKind.Company;
        }
        if (em.HasComponent<Game.Objects.Object>(e))
        {
            return em.HasComponent<Owner>(e) ? DlcHolderKind.SubObject : DlcHolderKind.PlacedObject;
        }
        if (em.HasComponent<Edge>(e) || em.HasComponent<Game.Net.Node>(e))
        {
            return DlcHolderKind.Net;
        }
        return em.HasComponent<Area>(e) ? DlcHolderKind.Area : DlcHolderKind.Other;
    }

    /// <summary>建物・置き物を単独で消してよいか（「欠落アセット」タブと同じ判定）。</summary>
    public static bool IsDeletable(EntityManager em, Entity e, DlcHolderKind kind) => kind switch
    {
        DlcHolderKind.Building => EntityRules.IsDeletable(em, e, MissingKind.Building),
        DlcHolderKind.PlacedObject => EntityRules.IsDeletable(em, e, MissingKind.Object),
        _ => false,
    };

    /// <summary>
    /// 付属物の持ち主（または持ち主の持ち主）が、同じ取り除きで消える物か。消えるなら付属物は持ち主と一緒に消えるので、数えも処理もしない。
    /// </summary>
    /// <remarks>
    /// 持ち主が車両・建物・置き物なら、ゲームの SubObjectSystem が持ち主の Deleted を見て子を深さ順に消す。持ち主が付属物
    /// （Object＋Owner）のときは、SubObjectSystem が Deleted のチャンクとしては飛ばす（その上の持ち主の組み直しで消える見込み）。
    /// 念のため取り除き側が <see cref="CollectSubObjectTree"/> で子をたどって一緒に消す。
    /// </remarks>
    /// <param name="isTarget">取り除く対象の参照先 prefab か。</param>
    public static bool OwnerWillBeRemoved(EntityManager em, Entity e, Func<Entity, bool> isTarget)
    {
        // 持ち主の入れ子は浅い（建物 → 付属物 → その付属物）ので、念のため段数を限る。
        for (var depth = 0; depth < MaxOwnerDepth; depth++)
        {
            if (!em.HasComponent<Owner>(e))
            {
                return false;
            }
            var owner = em.GetComponentData<Owner>(e).m_Owner;
            if (owner == Entity.Null || !em.Exists(owner) || !em.HasComponent<PrefabRef>(owner))
            {
                return false;
            }
            var kind = HolderKindOf(em, owner);
            if (isTarget(em.GetComponentData<PrefabRef>(owner).m_Prefab))
            {
                switch (kind)
                {
                    case DlcHolderKind.Vehicle:
                        return true;
                    case DlcHolderKind.SubObject:
                        // 中間の付属物が消えるのは、取り除きがそれを処理するとき（最上位の持ち主の prefab が欠落していないとき）だけ。
                        // 欠落していれば中間は一覧だけで残るので、さらに上の持ち主が消えるかを見る。
                        if (CanRebuildOwner(em, owner, out _))
                        {
                            return true;
                        }
                        break;
                    case DlcHolderKind.Building:
                    case DlcHolderKind.PlacedObject:
                        return IsDeletable(em, owner, kind);
                }
            }
            if (kind != DlcHolderKind.SubObject)
            {
                return false;
            }
            e = owner;
        }
        return false;
    }

    /// <summary>
    /// 持ち主として単独では組み直されない中間の付属物か（Object＋Owner で、建物・施設の拡張・車両・人ではない）。
    /// </summary>
    /// <remarks>
    /// ゲームの SubObjectSystem.CheckSubObjectOwnersJob は、この条件のチャンク（Temp でないもの）を持ち主として積まない（:492-495）。
    /// 中間の付属物の子は、最上位の持ち主の組み直しの中で、最上位の文脈（配置の条件・乱数）のまま深さ順に組み直される
    /// （UpdateSubObjectsJob.Execute の DeepOwners :1395-1453）。Attached の持ち主をたどる箇所（:550-554, 571-575）も、
    /// Object＋Owner で施設の拡張・建物でない間は上へたどってから積む。
    /// </remarks>
    public static bool IsIntermediateOwner(EntityManager em, Entity e) =>
        em.HasComponent<Owner>(e)
        && em.HasComponent<Game.Objects.Object>(e)
        && !em.HasComponent<Game.Buildings.ServiceUpgrade>(e)
        && !em.HasComponent<Building>(e)
        && !em.HasComponent<Game.Vehicles.Vehicle>(e)
        && !em.HasComponent<Game.Creatures.Creature>(e);

    /// <summary>
    /// 付属物の組み直しを頼む相手（最上位の持ち主）。直接の持ち主から、中間の付属物（<see cref="IsIntermediateOwner"/>）の間は上へたどる。
    /// 持ち主が無い・存在しないなら Entity.Null。
    /// </summary>
    /// <remarks>
    /// RentersUpdated の受け口（SubObjectSystem :464-474）は受け取った Entity をたどらずに深さ 0 の持ち主として組み直す。
    /// 中間の付属物へ送ると、その子が最上位の文脈なしで作り直され（ブランドなどの条件が効かない）、同じフレームに最上位も
    /// 組み直されると中間の付属物ごと消されて、新しく作られた子が持ち主を失うおそれがある。そのため最上位へ送る。
    /// </remarks>
    public static Entity TopOwner(EntityManager em, Entity subObject)
    {
        if (!em.HasComponent<Owner>(subObject))
        {
            return Entity.Null;
        }
        var owner = em.GetComponentData<Owner>(subObject).m_Owner;
        for (var depth = 0; depth < MaxOwnerDepth; depth++)
        {
            if (owner == Entity.Null || !em.Exists(owner))
            {
                return Entity.Null;
            }
            if (!IsIntermediateOwner(em, owner))
            {
                return owner;
            }
            owner = em.GetComponentData<Owner>(owner).m_Owner;
        }
        return owner != Entity.Null && em.Exists(owner) ? owner : Entity.Null;
    }

    /// <summary>
    /// 付属物を消して最上位の持ち主に組み直させてよいか。最上位の持ち主の prefab が欠落していれば false（一覧だけ）。
    /// </summary>
    /// <remarks>
    /// 欠落した prefab の持ち主（取り除かれない道路・単独で消せない建物など）を組み直させると、基本ゲームの付属物まで
    /// 作り直しの対象になる。持ち主がもう無い（<paramref name="top"/> が Null）なら、組み直す相手がいないので消すだけでよい。
    /// 持ち主が PrefabRef を持たなければ組み直させない（UpdateSubObjectsJob は持ち主の PrefabRef を必ず読む）。
    /// 欠落の判定は IsMissing そのもの（DLC に関係しない、mod 由来の欠落 prefab の持ち主も含める）。
    /// </remarks>
    public static bool CanRebuildOwner(EntityManager em, Entity subObject, out Entity top)
    {
        top = TopOwner(em, subObject);
        if (top == Entity.Null)
        {
            return true;
        }
        return em.HasComponent<PrefabRef>(top) && !IsMissing(em, em.GetComponentData<PrefabRef>(top).m_Prefab);
    }

    /// <summary>
    /// 付属物 <paramref name="root"/> の子孫（SubObject バッファをたどった、Owner が親を指す物）を <paramref name="into"/> へ足す。
    /// </summary>
    /// <remarks>
    /// ゲームは付属物を自分で消すとき、その子も「消える持ち主」として深さ順に消す（SubObjectSystem の RemoveUnusedOldSubObjects が
    /// DeepSubObjectOwnerData.m_Deleted を立てる）。外から付属物へ Deleted を付けた場合、SubObjectSystem は Object＋Owner の
    /// Deleted のチャンク自体は飛ばす。持ち主へ RentersUpdated を送れば組み直し（FillOldSubObjectsBuffer は Deleted を除かない）で
    /// 子まで消える見込みだが、実機で宙に浮いた Owner が残った（原因は未特定）ので、保険として同じ範囲を自分でたどって消す
    /// （ゲームの連鎖と結果は同じで、重ねて Deleted を付けても害はない）。
    /// 対象はゲームの FillOldSubObjectsBuffer と同じ（建物・施設の拡張・Secondary を除く。Secondary は SecondaryObjectSystem が
    /// 持ち主の Deleted を見て片付ける）。道路・エリア・レーンなどの子はゲームが持ち主の Deleted を見て消す。
    /// </remarks>
    /// <returns>新しく足した数。</returns>
    public static int CollectSubObjectTree(EntityManager em, Entity root, HashSet<Entity> into)
    {
        var added = 0;
        var stack = new Stack<(Entity Entity, int Depth)>();
        stack.Push((root, 0));
        while (stack.Count > 0)
        {
            var (parent, depth) = stack.Pop();
            // 入れ子は浅い（建物 → 付属物 → その付属物）ので、念のため段数を限る。
            if (depth >= 8 || !em.HasBuffer<Game.Objects.SubObject>(parent))
            {
                continue;
            }
            var children = em.GetBuffer<Game.Objects.SubObject>(parent, isReadOnly: true);
            for (var i = 0; i < children.Length; i++)
            {
                var child = children[i].m_SubObject;
                if (child == Entity.Null || !em.Exists(child) || !em.HasComponent<Owner>(child)
                    || em.GetComponentData<Owner>(child).m_Owner != parent
                    || em.HasComponent<Deleted>(child) || em.HasComponent<Temp>(child)
                    || em.HasComponent<Building>(child) || em.HasComponent<Game.Buildings.ServiceUpgrade>(child)
                    || em.HasComponent<Game.Objects.Secondary>(child))
                {
                    continue;
                }
                if (into.Add(child))
                {
                    added++;
                    stack.Push((child, depth + 1));
                }
            }
        }
        return added;
    }

    /// <summary>企業 prefab のブランド候補（CompanyBrandElement の並び順）。Game.Prefabs.CompanyInitializeSystem（:415-439）が作る。</summary>
    public List<DlcBrandCandidate> BrandCandidates(DlcDiagnostics d, Entity companyPrefab)
    {
        var em = m_EntityManager;
        var result = new List<DlcBrandCandidate>();
        if (!em.Exists(companyPrefab) || !em.HasBuffer<CompanyBrandElement>(companyPrefab))
        {
            return result;
        }
        var buffer = em.GetBuffer<CompanyBrandElement>(companyPrefab, isReadOnly: true);
        for (var i = 0; i < buffer.Length; i++)
        {
            var brand = buffer[i].m_Brand;
            result.Add(new DlcBrandCandidate(ToId(brand), brand == Entity.Null || MissingCached(brand), d.RequiredContentsOf(ToId(brand))));
        }
        return result;
    }

    /// <summary>付け替えで避けるコンテンツ（欠落しているもの全部）。</summary>
    public static HashSet<EntityId> ForbiddenContents(DlcDiagnostics d) => new(d.MissingContents());

    public static EntityId ToId(Entity e) => new(e.Index, e.Version);

    public static Entity ToEntity(EntityId id) => new() { Index = id.Index, Version = id.Version };

    private bool MissingCached(Entity prefab)
    {
        if (!m_MissingCache.TryGetValue(prefab, out var missing))
        {
            missing = IsMissing(m_EntityManager, prefab);
            m_MissingCache[prefab] = missing;
        }
        return missing;
    }

    private bool HasBrandCandidate(DlcDiagnostics d, Entity company)
    {
        var em = m_EntityManager;
        if (!em.HasComponent<PrefabRef>(company))
        {
            return false;
        }
        var prefab = em.GetComponentData<PrefabRef>(company).m_Prefab;
        if (!m_BrandCandidateCache.TryGetValue(prefab, out var has))
        {
            // 候補があるかは企業ごとの種に関係しないので、企業 prefab ごとに 1 回だけ選んでみる。
            has = DlcRemoval.SelectBrand(BrandCandidates(d, prefab), ForbiddenContents(d), 0) >= 0;
            m_BrandCandidateCache[prefab] = has;
        }
        return has;
    }

    private List<DlcContent> ReadContents(EntityManager em)
    {
        var result = new List<DlcContent>();
        using var entities = m_ContentQuery.ToEntityArray(Allocator.Temp);
        using var data = m_ContentQuery.ToComponentDataArray<ContentData>(Allocator.Temp);
        for (var i = 0; i < entities.Length; i++)
        {
            var e = entities[i];
            result.Add(new DlcContent(ToId(e), m_Prefabs.GetPrefabName(e), data[i].m_DlcID, (uint)data[i].m_Flags, IsMissing(em, e)));
        }
        return result;
    }

    private List<DlcPrerequisite> ReadPrerequisites(EntityManager em)
    {
        var result = new List<DlcPrerequisite>();
        using (var entities = m_ContentPrerequisiteQuery.ToEntityArray(Allocator.Temp))
        using (var data = m_ContentPrerequisiteQuery.ToComponentDataArray<ContentPrerequisiteData>(Allocator.Temp))
        {
            for (var i = 0; i < entities.Length; i++)
            {
                AddPrerequisite(em, result, entities[i], data[i].m_ContentPrerequisite, DlcReach.ContentPrerequisite);
            }
        }
        using (var entities = m_ModPrerequisiteQuery.ToEntityArray(Allocator.Temp))
        using (var data = m_ModPrerequisiteQuery.ToComponentDataArray<ModPrerequisiteData>(Allocator.Temp))
        {
            for (var i = 0; i < entities.Length; i++)
            {
                AddPrerequisite(em, result, entities[i], data[i].m_ContentPrerequisite, DlcReach.ModPrerequisite);
            }
        }
        return result;
    }

    private void AddPrerequisite(EntityManager em, List<DlcPrerequisite> result, Entity holder, Entity content, DlcReach kind)
    {
        if (content == Entity.Null)
        {
            return;
        }
        var enabled = em.HasComponent<PrefabData>(holder) && em.IsComponentEnabled<PrefabData>(holder);
        result.Add(new DlcPrerequisite(ToId(holder), m_Prefabs.GetPrefabName(holder), ToId(content), m_Prefabs.GetPrefabName(content), kind, enabled));
    }

    private void Scan<T>(DlcDiagnostics d, EntityQuery query, DlcRefPath path, Func<T, Entity> target, bool requirePrefabData = false)
        where T : unmanaged, IComponentData
    {
        var em = m_EntityManager;
        using var entities = query.ToEntityArray(Allocator.Temp);
        using var data = query.ToComponentDataArray<T>(Allocator.Temp);
        for (var i = 0; i < entities.Length; i++)
        {
            Visit(em, d, path, entities[i], target(data[i]), requirePrefabData);
        }
    }

    private void ScanBuffer<T>(DlcDiagnostics d, EntityQuery query, DlcRefPath path, Func<T, Entity> target, bool requirePrefabData = false)
        where T : unmanaged, IBufferElementData
    {
        var em = m_EntityManager;
        using var entities = query.ToEntityArray(Allocator.Temp);
        foreach (var holder in entities)
        {
            if (!em.HasBuffer<T>(holder))
            {
                continue;
            }
            var buffer = em.GetBuffer<T>(holder, isReadOnly: true);
            for (var j = 0; j < buffer.Length; j++)
            {
                Visit(em, d, path, holder, target(buffer[j]), requirePrefabData);
            }
        }
    }

    private void Visit(EntityManager em, DlcDiagnostics d, DlcRefPath path, Entity holder, Entity target, bool requirePrefabData)
    {
        if (target == Entity.Null || (requirePrefabData && (!em.Exists(target) || !em.HasComponent<PrefabData>(target))))
        {
            return;
        }
        var targetId = ToId(target);
        // 分類（HasComponent の連打）は、コンテンツへつながる参照だけに行う。
        if (!d.IsRelevant(targetId))
        {
            return;
        }
        var missing = MissingCached(target);
        DlcClass cls;
        var jump = holder;
        switch (path)
        {
            case DlcRefPath.PrefabRef:
            {
                var kind = HolderKindOf(em, holder);
                var canAct = IsDeletable(em, holder, kind);
                if (kind == DlcHolderKind.SubObject)
                {
                    // 持ち主ごと消え、持ち主の参照先が付属物と同じコンテンツをすべて覆うなら、持ち主の側で数える（同じ物を二重に数えない）。
                    // 覆わない（付属物だけが別のコンテンツにつながる）なら、予測から落ちないように付属物も数える。
                    if (missing && OwnerWillBeRemoved(em, holder, p => d.IsRelevant(ToId(p)) && MissingCached(p) && d.Covers(ToId(p), targetId)))
                    {
                        return;
                    }
                    // 取り除きと同じ判定（DlcRemovalSystem）。最上位の持ち主の prefab が欠落していれば一覧だけ。ただし、その持ち主が
                    // 欠落したコンテンツとして取り除かれる（欠落している全コンテンツを取り除けば一緒に消える）なら取り除ける側に数える。
                    canAct = CanRebuildOwner(em, holder, out var top)
                        || (missing && OwnerWillBeRemoved(em, holder, p => d.IsRelevant(ToId(p)) && MissingCached(p)));
                    // 「見に行く」では最上位の持ち主（建物など）を選ぶ。付属物そのものは選んでも情報パネルが出ない。
                    jump = top != Entity.Null ? top : em.GetComponentData<Owner>(holder).m_Owner;
                }
                else if (kind == DlcHolderKind.Company && em.HasComponent<PropertyRenter>(holder))
                {
                    // 企業そのものは位置を持たないので、入居している建物を選ぶ。
                    jump = em.GetComponentData<PropertyRenter>(holder).m_Property;
                }
                cls = DlcRemoval.Classify(path, kind, missing, canAct);
                break;
            }
            case DlcRefPath.CompanyBrand:
            {
                // 企業 prefab そのものが欠落した企業は、取り除きで企業ごと転出させる（ブランドの参照も一緒に消える）。
                var companyPrefab = em.HasComponent<PrefabRef>(holder) ? em.GetComponentData<PrefabRef>(holder).m_Prefab : Entity.Null;
                var movesAway = missing && companyPrefab != Entity.Null && MissingCached(companyPrefab);
                if (movesAway && d.IsRelevant(ToId(companyPrefab)) && d.Covers(ToId(companyPrefab), targetId))
                {
                    // 企業 prefab の参照（カテゴリ「企業」）がブランドと同じコンテンツをすべて覆うなら、企業の側で数える（同じ企業を二重に数えない）。
                    return;
                }
                cls = DlcRemoval.Classify(path, DlcHolderKind.Company, missing, missing && !movesAway && HasBrandCandidate(d, holder), movesAway);
                // 企業そのものは位置を持たないので、入居している建物を選ぶ。
                if (em.HasComponent<PropertyRenter>(holder))
                {
                    jump = em.GetComponentData<PropertyRenter>(holder).m_Property;
                }
                break;
            }
            default:
                cls = DlcRemoval.Classify(path, DlcHolderKind.Other, missing, canAct: false);
                break;
        }
        if (!d.Record(path, targetId, cls))
        {
            return;
        }
        // 見本が要るときだけ、保有 Entity の prefab 名と位置を引く。
        var holderPrefab = em.HasComponent<PrefabRef>(holder) ? m_Prefabs.GetPrefabName(em.GetComponentData<PrefabRef>(holder).m_Prefab) : string.Empty;
        var has = EntityRules.HasPosition(em, holder);
        var p = has ? EntityRules.PositionOf(em, holder) : default;
        var sample = new DlcReferenceSample(ToId(holder), holderPrefab, has, p.x, p.y, p.z);
        var jumpSample = sample;
        if (jump != holder && jump != Entity.Null && em.Exists(jump))
        {
            var jumpHas = has || EntityRules.HasPosition(em, jump);
            var jp = has ? p : jumpHas ? EntityRules.PositionOf(em, jump) : default;
            jumpSample = new DlcReferenceSample(ToId(jump), holderPrefab, jumpHas, jp.x, jp.y, jp.z);
        }
        d.AddSample(path, targetId, cls, sample, jumpSample);
    }
}
