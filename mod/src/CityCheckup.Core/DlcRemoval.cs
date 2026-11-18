using System;
using System.Collections.Generic;
using System.Linq;

namespace CityCheckup.Core;

/// <summary>PrefabRef で DLC の prefab を参照している Entity の種類（処置を決めるための分類）。</summary>
public enum DlcHolderKind
{
    /// <summary>車両（Game.Vehicles.Vehicle）。連結車両は 1 両ずつ数える。</summary>
    Vehicle,

    /// <summary>持ち主（Owner）のある物。看板など、建物や道路に付属してゲームが組み立てる物。</summary>
    SubObject,

    /// <summary>建物。</summary>
    Building,

    /// <summary>持ち主のない置き物（プレイヤーが置いた小物・木など）。</summary>
    PlacedObject,

    /// <summary>道路・線路の区間と交点。</summary>
    Net,

    /// <summary>区画・地表などのエリア。</summary>
    Area,

    /// <summary>企業（CompanyData を持つ Entity の PrefabRef が企業 prefab を指す）。</summary>
    Company,

    /// <summary>人・動物（Game.Creatures.Creature）。</summary>
    Creature,

    /// <summary>上のどれでもないもの（路線・世帯・市民など）。</summary>
    Other,
}

/// <summary>DLC タブで見せるカテゴリ。</summary>
public enum DlcCategory
{
    /// <summary>車両。</summary>
    Vehicle,

    /// <summary>企業のブランド（CompanyData.m_Brand）。</summary>
    Brand,

    /// <summary>看板などの付属物（Owner のある物）。</summary>
    SubObject,

    /// <summary>路線の車両指定（VehicleModel）。</summary>
    RouteModel,

    /// <summary>建物・持ち主のない置き物。</summary>
    Building,

    /// <summary>道路・エリア（見に行くだけ）。</summary>
    NetArea,

    /// <summary>企業そのもの（企業 prefab が欠落）。</summary>
    Company,

    /// <summary>
    /// 街の設定・統計の記録（システムが持つ参照: テーマ・マップの要求コンテンツ・気候・地形と水の描画設定・建物ごとの輸送の記録）。一覧だけ。
    /// </summary>
    System,

    /// <summary>その他（未対応）。</summary>
    Other,
}

/// <summary>カテゴリに対して mod が行う処置。</summary>
public enum DlcAction
{
    /// <summary>Deleted を付けて消す（車両は編成ごと）。</summary>
    Delete,

    /// <summary>ブランドを、欠落していない別のブランドへ付け替える。</summary>
    Rebrand,

    /// <summary>路線の車両指定から欠落した prefab を外す。</summary>
    ClearModel,

    /// <summary>付属物を消し、持ち主に組み直させる。</summary>
    RebuildDelete,

    /// <summary>一覧と「見に行く」だけ（この mod では取り除かない）。</summary>
    ListOnly,

    /// <summary>DLC が有効なまま（参照先が欠落していない）。DLC を無効にして読み込み直せば取り除ける。</summary>
    NeedsDlcDisabled,

    /// <summary>付け替え先のブランドが無い。Entity.Null にはせず、そのまま残す。</summary>
    NoCandidate,

    /// <summary>
    /// 企業を転出させる（企業 prefab が欠落した企業）。ゲームの転出と同じく物件を空き物件にして企業に Deleted を付ける。
    /// その企業のブランドの参照も一緒に消える。
    /// </summary>
    MoveAway,
}

/// <summary>参照 1 件の分類（カテゴリ・処置・保有 Entity の種類・参照先が欠落しているか）。</summary>
public readonly struct DlcClass : IEquatable<DlcClass>
{
    public DlcClass(DlcCategory category, DlcAction action, DlcHolderKind kind, bool targetMissing)
    {
        Category = category;
        Action = action;
        Kind = kind;
        TargetMissing = targetMissing;
    }

    public DlcCategory Category { get; }

    public DlcAction Action { get; }

    public DlcHolderKind Kind { get; }

    public bool TargetMissing { get; }

    public bool Equals(DlcClass other) =>
        Category == other.Category && Action == other.Action && Kind == other.Kind && TargetMissing == other.TargetMissing;

    public override bool Equals(object? obj) => obj is DlcClass other && Equals(other);

    public override int GetHashCode() => ((((int)Category * 31) + (int)Action) * 31 + (int)Kind) * 2 + (TargetMissing ? 1 : 0);
}

/// <summary>ブランドの付け替え先の候補 1 つ（企業 prefab の CompanyBrandElement の要素）。</summary>
public readonly struct DlcBrandCandidate
{
    public DlcBrandCandidate(EntityId brand, bool missing, IReadOnlyCollection<EntityId> requiredContents)
    {
        Brand = brand;
        Missing = missing;
        RequiredContents = requiredContents;
    }

    public EntityId Brand { get; }

    /// <summary>ブランドの prefab が欠落しているか（PrefabData が無効、または m_Index が負）。</summary>
    public bool Missing { get; }

    /// <summary>このブランドが ContentPrerequisiteData / ModPrerequisiteData で指すコンテンツ。</summary>
    public IReadOnlyCollection<EntityId> RequiredContents { get; }
}

/// <summary>
/// DLC を外した街から、DLC を要求する原因になっている参照を取り除くときの規則（ゲームの型に依存しない部分）。
/// </summary>
/// <remarks>
/// 取り除くのは「参照先の prefab が欠落しているもの」だけ。DLC が有効なまま（欠落していない）だと、ゲームが
/// 車両の選び直し・付属物の組み直し・企業の初期化で同じ prefab を選び直すため、取り除いても意味がない。
/// 道路・エリアはゲームのツールを通した削除が要り、方針・予算・チャープなどは街の仕組みに組み込まれているので、
/// この mod では一覧だけにする。システムが持つ参照（街の設定・統計の記録）も一覧だけ。とくに建物ごとの輸送の記録は、
/// 輸送の実績による解除条件（Game.Prefabs.TransportRequirementSystem）が読むので消さない。
/// </remarks>
public static class DlcRemoval
{
    /// <summary>
    /// 参照 1 件の分類を決める。
    /// </summary>
    /// <param name="path">参照の経路。</param>
    /// <param name="kind">保有 Entity の種類（PrefabRef 以外の経路では使わない）。</param>
    /// <param name="targetMissing">参照先の prefab が欠落しているか。</param>
    /// <param name="canAct">
    /// 建物・置き物: 単独で消してよいか（ゲームの「削除」ボタンと同じ判定）。ブランド: 付け替え先があるか。
    /// 付属物: 最上位の持ち主に組み直させてよいか（最上位の持ち主の prefab が欠落していない。欠落した持ち主を組み直させると、
    /// 基本ゲームの付属物まで作り直しの対象になるので、そのときは一覧だけにする）。それ以外では使わない。
    /// </param>
    /// <param name="holderMovesAway">
    /// ブランド: ブランドを持つ企業そのものの prefab が欠落していて、企業ごと転出させるか。転出すればブランドの参照も消えるので、
    /// 付け替え先の有無に関係なく転出として扱う。それ以外では使わない。
    /// </param>
    public static DlcClass Classify(DlcRefPath path, DlcHolderKind kind, bool targetMissing, bool canAct, bool holderMovesAway = false)
    {
        var category = CategoryOf(path, kind);
        var action = category switch
        {
            DlcCategory.Vehicle => DlcAction.Delete,
            DlcCategory.SubObject => canAct ? DlcAction.RebuildDelete : DlcAction.ListOnly,
            DlcCategory.Building => canAct ? DlcAction.Delete : DlcAction.ListOnly,
            DlcCategory.Brand => holderMovesAway ? DlcAction.MoveAway : canAct ? DlcAction.Rebrand : DlcAction.NoCandidate,
            DlcCategory.RouteModel => DlcAction.ClearModel,
            DlcCategory.Company => DlcAction.MoveAway,
            _ => DlcAction.ListOnly,
        };
        // 一覧だけのものは DLC の有無に関係なく一覧だけ。取り除けるものでも参照先が欠落していなければ、
        // ゲームが作り直すので「DLC を無効にして読み込み直す」案内にする。
        if (action != DlcAction.ListOnly && !targetMissing)
        {
            action = DlcAction.NeedsDlcDisabled;
        }
        return new DlcClass(category, action, kind, targetMissing);
    }

    /// <summary>この処置で参照が無くなる（mod が取り除ける）か。</summary>
    public static bool IsRemovable(DlcAction action) =>
        action is DlcAction.Delete or DlcAction.Rebrand or DlcAction.ClearModel or DlcAction.RebuildDelete or DlcAction.MoveAway;

    public static DlcCategory CategoryOf(DlcRefPath path, DlcHolderKind kind) => path switch
    {
        DlcRefPath.PrefabRef => kind switch
        {
            DlcHolderKind.Vehicle => DlcCategory.Vehicle,
            DlcHolderKind.SubObject => DlcCategory.SubObject,
            DlcHolderKind.Building or DlcHolderKind.PlacedObject => DlcCategory.Building,
            DlcHolderKind.Net or DlcHolderKind.Area => DlcCategory.NetArea,
            DlcHolderKind.Company => DlcCategory.Company,
            _ => DlcCategory.Other,
        },
        DlcRefPath.CompanyBrand => DlcCategory.Brand,
        DlcRefPath.VehicleModelPrimary or DlcRefPath.VehicleModelSecondary => DlcCategory.RouteModel,
        DlcRefPath.CityTheme or DlcRefPath.CityRequiredContent or DlcRefPath.Climate
            or DlcRefPath.TerrainRenderSettings or DlcRefPath.WaterRenderSettings or DlcRefPath.TransportUsage => DlcCategory.System,
        // UnderConstruction・Policy・ServiceBudget・EditorContainer・Chirp・SubReplacement は未対応。
        _ => DlcCategory.Other,
    };

    /// <summary>
    /// ブランドの付け替え先を候補から決定的に 1 つ選ぶ。候補が無ければ -1（呼び出し側は付け替えずに残す）。
    /// </summary>
    /// <remarks>
    /// 欠落したブランドは選ばない。まず、どのコンテンツへの前提も持たないブランド（基本ゲームのブランド）から選ぶ。
    /// 有効な DLC のブランドへ付け替えると、別の DLC の要求を新しく作ってしまうため。それが無いときだけ、
    /// 取り除こうとしているコンテンツ（<paramref name="forbiddenContents"/>）への前提を持たないブランドから選ぶ。
    /// ゲームは企業の初期化でブランドを無作為に選ぶ（Game.Citizens.CompanyInitializeSystem）が、ここでは同じ街なら
    /// 同じ結果になるよう、企業ごとの種で選ぶ。Entity.Null は名前表示が壊れるので選ばない。
    /// </remarks>
    /// <returns><paramref name="candidates"/> の添字。候補が無ければ -1。</returns>
    public static int SelectBrand(IReadOnlyList<DlcBrandCandidate> candidates, ICollection<EntityId> forbiddenContents, uint seed)
    {
        var plain = new List<int>();
        var allowed = new List<int>();
        for (var i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            if (c.Missing || c.Brand.Equals(default(EntityId)))
            {
                continue;
            }
            if (c.RequiredContents.Count == 0)
            {
                plain.Add(i);
            }
            if (!c.RequiredContents.Any(forbiddenContents.Contains))
            {
                allowed.Add(i);
            }
        }
        var pool = plain.Count > 0 ? plain : allowed;
        return pool.Count == 0 ? -1 : pool[(int)(seed % (uint)pool.Count)];
    }

    /// <summary>
    /// 路線の車両指定（VehicleModel の並び）から、取り除く prefab を指す欄だけを Null（既定の EntityId）にし、
    /// 主・副とも Null になった要素を除く。全部消えれば空になる（ゲームの「選択を外す」と同じ扱い）。
    /// </summary>
    /// <param name="cleared">Null にした欄の数。</param>
    public static List<(EntityId Primary, EntityId Secondary)> RewriteVehicleModels(
        IReadOnlyList<(EntityId Primary, EntityId Secondary)> models,
        Func<EntityId, bool> remove,
        out int cleared)
    {
        var result = new List<(EntityId, EntityId)>(models.Count);
        cleared = 0;
        foreach (var (primary, secondary) in models)
        {
            var p = primary;
            var s = secondary;
            if (!p.Equals(default(EntityId)) && remove(p))
            {
                p = default;
                cleared++;
            }
            if (!s.Equals(default(EntityId)) && remove(s))
            {
                s = default;
                cleared++;
            }
            if (p.Equals(default(EntityId)) && s.Equals(default(EntityId)))
            {
                continue;
            }
            result.Add((p, s));
        }
        return result;
    }

    public static string CategoryKey(DlcCategory category) => category switch
    {
        DlcCategory.Vehicle => "vehicle",
        DlcCategory.Brand => "brand",
        DlcCategory.SubObject => "subObject",
        DlcCategory.RouteModel => "routeModel",
        DlcCategory.Building => "building",
        DlcCategory.NetArea => "netArea",
        DlcCategory.Company => "company",
        DlcCategory.System => "system",
        _ => "other",
    };

    public static string ActionKey(DlcAction action) => action switch
    {
        DlcAction.Delete => "delete",
        DlcAction.Rebrand => "rebrand",
        DlcAction.ClearModel => "clearModel",
        DlcAction.RebuildDelete => "rebuildDelete",
        DlcAction.NeedsDlcDisabled => "needsDlcDisabled",
        DlcAction.NoCandidate => "noCandidate",
        DlcAction.MoveAway => "moveAway",
        _ => "listOnly",
    };

    public static string KindKey(DlcHolderKind kind) => kind switch
    {
        DlcHolderKind.Vehicle => "vehicle",
        DlcHolderKind.SubObject => "subObject",
        DlcHolderKind.Building => "building",
        DlcHolderKind.PlacedObject => "placedObject",
        DlcHolderKind.Net => "net",
        DlcHolderKind.Area => "area",
        DlcHolderKind.Company => "company",
        DlcHolderKind.Creature => "creature",
        _ => "other",
    };
}
