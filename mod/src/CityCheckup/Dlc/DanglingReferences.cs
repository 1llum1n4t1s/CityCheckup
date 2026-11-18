using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Game.Areas;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Companies;
using Game.Creatures;
using Game.Net;
using Game.Prefabs;
using Game.Routes;
using Game.Tools;
using Game.Vehicles;
using Unity.Collections;
using Unity.Entities;

namespace CityCheckup.Dlc;

/// <summary>
/// 参照先が存在しない（宙に浮いた）参照を数え、内訳を Player.log 向けの行にする。削除の後始末が漏れていないかの確認用（読むだけ）。
/// 取り除きの動作確認（debug-dlc-remove）と、読み込み直したセーブの診断（debug-dlc）で共有する。
/// </summary>
/// <remarks>
/// 実機の確認は回数が限られるので、件数だけでなく「保有 Entity の prefab 名＋主要コンポーネント」ごとの件数と見本を出し、
/// 1 回のログで原因を絞れるようにする。
/// CarKeeper は有効・無効を分けて数える。ゲームは車を手放すとき（CitizenBehaviorSystem.ReleaseCar）CarKeeper を無効にするだけで
/// m_Car を残し、その車が後で消えても PersonalCar.m_Keeper からしか片付けない（Game.Vehicles.ReferencesSystem）。無効な CarKeeper の
/// m_Car はどこからも読まれず（読む側は IsComponentEnabled を先に見る）、CarKeeper は IEmptySerializable なので保存もされない
/// （読み込み時に Game.Serialization.CarKeeperSystem が組み直す）。そのため問題になるのは有効な CarKeeper だけ。
/// 保存時に消えた Entity への参照が Null として書かれる場合に備え、Owner が Null の件数も別に出す（読み込み直したセーブ向け）。
/// </remarks>
internal static class DanglingReferences
{
    /// <summary>内訳を出すまとまりの数（件数の多い順）。</summary>
    private const int GroupsPerKind = 8;

    /// <summary>1 行目に集計、2 行目以降に内訳を返す。</summary>
    /// <param name="companies">企業の照会（ブランドが Null の企業を数える）。</param>
    public static IReadOnlyList<string> Describe(
        EntityManager em, PrefabSystem prefabs, EntityQuery companies, IReadOnlyDictionary<Entity, string>? removed = null)
    {
        var lines = new List<string>();
        var detail = new List<string>();

        var (owner, ownerNull) = OwnerReferences(em, prefabs, removed, detail);
        var (keeperEnabled, keeperDisabled) = CarKeeperReferences(em, removed, detail);
        var currentVehicle = Dangling<CurrentVehicle>(em, prefabs, removed, "currentVehicle", c => c.m_Vehicle, detail);
        var ownedVehicle = DanglingBuffer<OwnedVehicle>(em, prefabs, removed, "ownedVehicle", v => v.m_Vehicle, detail);
        var routeVehicle = DanglingBuffer<RouteVehicle>(em, prefabs, removed, "routeVehicle", v => v.m_Vehicle, detail);
        // 企業を転出させた後始末の検査（従業員の勤め先と、物件の入居者一覧。ゲームの RemovedSystem が片付けるはずのもの）。
        var workplace = Dangling<Worker>(em, prefabs, removed, "workerWorkplace", w => w.m_Workplace, detail);
        var renter = DanglingBuffer<Renter>(em, prefabs, removed, "renter", r => r.m_Renter, detail);
        var nullBrand = 0;
        using (var data = companies.ToComponentDataArray<CompanyData>(Allocator.Temp))
        {
            foreach (var c in data)
            {
                if (c.m_Brand == Entity.Null)
                {
                    nullBrand++;
                }
            }
        }

        lines.Add($"dangling owner={N(owner)} carKeeper={N(keeperEnabled)} currentVehicle={N(currentVehicle)} ownedVehicle={N(ownedVehicle)} "
            + $"routeVehicle={N(routeVehicle)} workerWorkplace={N(workplace)} renter={N(renter)} companiesWithNullBrand={N(nullBrand)} "
            + $"| not counted as dangling: carKeeperDisabledStale={N(keeperDisabled)} ownerNull={N(ownerNull)}");
        lines.AddRange(detail);
        return lines;
    }

    private static string N(int n) => n.ToString(CultureInfo.InvariantCulture);

    private static string Id(Entity e) => $"{e.Index.ToString(CultureInfo.InvariantCulture)}:{e.Version.ToString(CultureInfo.InvariantCulture)}";

    private static EntityQuery Query<T>(EntityManager em, bool ignoreEnabled = false) => em.CreateEntityQuery(new EntityQueryDesc
    {
        All = new[] { ComponentType.ReadOnly<T>() },
        None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
        Options = ignoreEnabled ? EntityQueryOptions.IgnoreComponentEnabledState : EntityQueryOptions.Default,
    });

    /// <summary>Owner の参照先が存在しないもの（宙に浮いた）と、Null のもの。どちらも保有 Entity の種類ごとに内訳を出す。</summary>
    private static (int Dangling, int Null) OwnerReferences(
        EntityManager em, PrefabSystem prefabs, IReadOnlyDictionary<Entity, string>? removed, List<string> detail)
    {
        using var query = Query<Owner>(em);
        using var entities = query.ToEntityArray(Allocator.Temp);
        using var data = query.ToComponentDataArray<Owner>(Allocator.Temp);
        var dangling = new Groups(removed);
        var nulls = new Groups(removed);
        for (var i = 0; i < entities.Length; i++)
        {
            var target = data[i].m_Owner;
            if (target == Entity.Null)
            {
                nulls.Add(Describe(em, prefabs, entities[i]), entities[i], target);
            }
            else if (!em.Exists(target))
            {
                dangling.Add(Describe(em, prefabs, entities[i]), entities[i], target);
            }
        }
        dangling.AppendTo(detail, "owner");
        nulls.AppendTo(detail, "ownerNull");
        return (dangling.Total, nulls.Total);
    }

    /// <summary>CarKeeper.m_Car の参照先が存在しないもの。有効・無効を分ける（無効なものはゲームでも残る古い値）。</summary>
    private static (int Enabled, int Disabled) CarKeeperReferences(EntityManager em, IReadOnlyDictionary<Entity, string>? removed, List<string> detail)
    {
        using var query = Query<CarKeeper>(em, ignoreEnabled: true);
        using var entities = query.ToEntityArray(Allocator.Temp);
        using var data = query.ToComponentDataArray<CarKeeper>(Allocator.Temp);
        var enabled = new Groups(removed);
        var disabled = new Groups(removed);
        for (var i = 0; i < entities.Length; i++)
        {
            var car = data[i].m_Car;
            if (car == Entity.Null || em.Exists(car))
            {
                continue;
            }
            var citizen = entities[i];
            var key = $"citizen household={(em.HasComponent<HouseholdMember>(citizen) ? "yes" : "no")} "
                + $"currentTransport={(em.HasComponent<CurrentTransport>(citizen) ? "yes" : "no")}";
            (em.IsComponentEnabled<CarKeeper>(citizen) ? enabled : disabled).Add(key, citizen, car);
        }
        enabled.AppendTo(detail, "carKeeper(enabled)");
        disabled.AppendTo(detail, "carKeeper(disabled, stale)");
        return (enabled.Total, disabled.Total);
    }

    private static int Dangling<T>(
        EntityManager em, PrefabSystem prefabs, IReadOnlyDictionary<Entity, string>? removed, string label, Func<T, Entity> target, List<string> detail)
        where T : unmanaged, IComponentData
    {
        using var query = Query<T>(em);
        using var entities = query.ToEntityArray(Allocator.Temp);
        using var data = query.ToComponentDataArray<T>(Allocator.Temp);
        var groups = new Groups(removed);
        for (var i = 0; i < entities.Length; i++)
        {
            var e = target(data[i]);
            if (e != Entity.Null && !em.Exists(e))
            {
                groups.Add(Describe(em, prefabs, entities[i]), entities[i], e);
            }
        }
        groups.AppendTo(detail, label);
        return groups.Total;
    }

    private static int DanglingBuffer<T>(
        EntityManager em, PrefabSystem prefabs, IReadOnlyDictionary<Entity, string>? removed, string label, Func<T, Entity> target, List<string> detail)
        where T : unmanaged, IBufferElementData
    {
        using var query = Query<T>(em);
        using var entities = query.ToEntityArray(Allocator.Temp);
        var groups = new Groups(removed);
        foreach (var holder in entities)
        {
            var buffer = em.GetBuffer<T>(holder, isReadOnly: true);
            for (var i = 0; i < buffer.Length; i++)
            {
                var e = target(buffer[i]);
                if (e != Entity.Null && !em.Exists(e))
                {
                    groups.Add(Describe(em, prefabs, holder), holder, e);
                }
            }
        }
        groups.AppendTo(detail, label);
        return groups.Total;
    }

    /// <summary>保有 Entity の prefab 名と主要コンポーネント（内訳のまとまりの鍵）。</summary>
    private static string Describe(EntityManager em, PrefabSystem prefabs, Entity e)
    {
        var name = em.HasComponent<PrefabRef>(e) ? prefabs.GetPrefabName(em.GetComponentData<PrefabRef>(e).m_Prefab) : "(no PrefabRef)";
        var parts = new List<string>();
        void Flag<T>(string label)
        {
            if (em.HasComponent<T>(e))
            {
                parts.Add(label);
            }
        }
        Flag<Game.Objects.Object>("Object");
        Flag<Game.Objects.Static>("Static");
        Flag<Game.Objects.Secondary>("Secondary");
        Flag<Building>("Building");
        Flag<Vehicle>("Vehicle");
        Flag<Creature>("Creature");
        Flag<Lane>("Lane");
        Flag<Edge>("Edge");
        Flag<Game.Net.Node>("Node");
        Flag<Area>("Area");
        Flag<Game.Notifications.Icon>("Icon");
        Flag<CompanyData>("Company");
        Flag<Citizen>("Citizen");
        Flag<Route>("Route");
        if (em.HasBuffer<Game.Objects.SubObject>(e))
        {
            parts.Add("SubObjects");
        }
        if (em.HasBuffer<Game.Net.SubLane>(e))
        {
            parts.Add("SubLanes");
        }
        return $"'{name}' [{string.Join(",", parts)}]";
    }

    /// <summary>種類ごとの件数と見本（最初の 1 件）。</summary>
    private sealed class Groups
    {
        private readonly Dictionary<string, (int Count, Entity Holder, Entity Target)> m_Groups = new(StringComparer.Ordinal);
        private readonly IReadOnlyDictionary<Entity, string>? m_Removed;

        /// <param name="removed">取り除きで消した Entity の種類（動作確認のときだけ）。あれば参照先が何だったかを鍵に足す。</param>
        public Groups(IReadOnlyDictionary<Entity, string>? removed)
        {
            m_Removed = removed;
        }

        public int Total { get; private set; }

        public void Add(string key, Entity holder, Entity target)
        {
            if (m_Removed is not null && target != Entity.Null)
            {
                key += " targetWas=" + (m_Removed.TryGetValue(target, out var was) ? was : "(not removed by City Checkup)");
            }
            Total++;
            m_Groups[key] = m_Groups.TryGetValue(key, out var g) ? (g.Count + 1, g.Holder, g.Target) : (1, holder, target);
        }

        public void AppendTo(List<string> lines, string label)
        {
            if (Total == 0)
            {
                return;
            }
            var shown = m_Groups.OrderByDescending(g => g.Value.Count).ThenBy(g => g.Key, StringComparer.Ordinal).Take(GroupsPerKind).ToList();
            lines.Add($"dangling {label}: total={N(Total)} kinds={N(m_Groups.Count)} (top {N(shown.Count)})");
            foreach (var g in shown)
            {
                lines.Add($"dangling   {label} x{N(g.Value.Count)} holder={g.Key} e.g. entity={Id(g.Value.Holder)} -> {Id(g.Value.Target)}");
            }
        }
    }
}
