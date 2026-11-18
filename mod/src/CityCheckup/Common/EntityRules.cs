using Game.Areas;
using Game.Buildings;
using Game.Common;
using Game.Net;
using CityCheckup.Core;
using Unity.Entities;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;

namespace CityCheckup.Common;

/// <summary>
/// 街の Entity の種類・単独で消してよいか・位置の判定。「欠落アセット」タブと「DLC」タブで共有する。
/// </summary>
internal static class EntityRules
{
    /// <summary>置かれた物（物・建物・道路の区間・エリア）の種類。</summary>
    public static MissingKind KindOf(EntityManager em, Entity e)
    {
        if (em.HasComponent<Building>(e))
        {
            return MissingKind.Building;
        }
        if (em.HasComponent<Game.Objects.Object>(e))
        {
            return MissingKind.Object;
        }
        return em.HasComponent<Edge>(e) ? MissingKind.Net : MissingKind.Area;
    }

    /// <summary>ゲームの情報パネルの「削除」ボタンが使える条件に合わせた判定（成長型の建物だけ緩めている）。</summary>
    /// <remarks>
    /// 成長型の建物は、欠落していれば作り直されないため、ここでは消してよいものとして扱う。
    /// 道路・線路とエリアはゲームのツールを通した削除が必要なので消さない。
    /// </remarks>
    public static bool IsDeletable(EntityManager em, Entity e, MissingKind kind)
    {
        if (em.HasComponent<Game.Objects.OutsideConnection>(e))
        {
            return false;
        }
        switch (kind)
        {
            case MissingKind.Building:
                if (em.HasComponent<WaterPowered>(e))
                {
                    return false;
                }
                // 付属建物は本体ごとでないと消せない。ただし施設の拡張（ServiceUpgrade）は単独で消せる。
                return !em.HasComponent<Owner>(e) || em.HasComponent<ServiceUpgrade>(e);
            case MissingKind.Object:
                return em.HasComponent<Game.Objects.Static>(e) && !em.HasComponent<Native>(e) && !em.HasComponent<Owner>(e);
            default:
                return false;
        }
    }

    /// <summary>位置を持つ種類の Entity か（PositionOf が 0 を返すのが「原点」なのか「位置なし」なのかを見分ける）。</summary>
    public static bool HasPosition(EntityManager em, Entity e) =>
        em.HasComponent<Transform>(e) || em.HasComponent<Curve>(e) || em.HasComponent<Geometry>(e);

    public static float3 PositionOf(EntityManager em, Entity e)
    {
        if (em.HasComponent<Transform>(e))
        {
            return em.GetComponentData<Transform>(e).m_Position;
        }
        if (em.HasComponent<Curve>(e))
        {
            // 区間の曲線の中ほど（ベジェ曲線の t=0.5）。
            var b = em.GetComponentData<Curve>(e).m_Bezier;
            return (b.a + 3f * b.b + 3f * b.c + b.d) / 8f;
        }
        if (em.HasComponent<Geometry>(e))
        {
            return em.GetComponentData<Geometry>(e).m_CenterPosition;
        }
        return float3.zero;
    }
}
