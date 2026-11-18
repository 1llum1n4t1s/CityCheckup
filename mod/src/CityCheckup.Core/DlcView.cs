using System;
using System.Collections.Generic;
using System.Linq;

namespace CityCheckup.Core;

/// <summary>コンテンツの参照先の状態。</summary>
public enum DlcContentStatus
{
    /// <summary>参照先がすべて欠落（DLC を外して読み込んだ）。取り除ける。</summary>
    Missing,

    /// <summary>参照先がすべて有効（DLC が有効）。DLC を無効にして読み込み直せば取り除ける。</summary>
    Loaded,

    /// <summary>欠落したものと有効なものが混ざっている。</summary>
    Mixed,
}

/// <summary>DLC タブのカテゴリ 1 つ（同じコンテンツ・カテゴリ・処置の参照のまとまり）。</summary>
public sealed class DlcCategoryView
{
    private readonly List<(string Name, int Count)> m_Targets = new();
    private readonly List<DlcReferenceSample> m_Jumps = new();

    public DlcCategoryView(string key, DlcCategory category, DlcAction action)
    {
        Key = key;
        Category = category;
        Action = action;
    }

    /// <summary>UI との受け渡しに使う識別子（コンテンツ名|カテゴリ|処置。コンテンツをまたいで一意）。</summary>
    public string Key { get; }

    public DlcCategory Category { get; }

    public DlcAction Action { get; }

    public int Count { get; private set; }

    /// <summary>参照先 prefab の名前と件数（件数の多い順に足される）。</summary>
    public IReadOnlyList<(string Name, int Count)> Targets => m_Targets;

    /// <summary>「見に行く」先（上限 <see cref="DlcDiagnostics.JumpsPerCategory"/>）。</summary>
    public IReadOnlyList<DlcReferenceSample> Jumps => m_Jumps;

    internal void Add(string targetName, int count, IEnumerable<DlcReferenceSample> jumps)
    {
        Count += count;
        var i = m_Targets.FindIndex(t => string.Equals(t.Name, targetName, StringComparison.Ordinal));
        if (i >= 0)
        {
            m_Targets[i] = (targetName, m_Targets[i].Count + count);
        }
        else
        {
            m_Targets.Add((targetName, count));
        }
        foreach (var j in jumps)
        {
            if (m_Jumps.Count >= DlcDiagnostics.JumpsPerCategory)
            {
                break;
            }
            m_Jumps.Add(j);
        }
    }
}

/// <summary>DLC タブのコンテンツ 1 つ。</summary>
public sealed class DlcContentView
{
    public DlcContentView(DlcContent content, DlcContentStatus status, bool remainsAfter, IReadOnlyList<DlcCategoryView> categories)
    {
        Content = content;
        Status = status;
        RemainsAfter = remainsAfter;
        Categories = categories;
    }

    public DlcContent Content { get; }

    /// <summary>UI との受け渡しに使う識別子（コンテンツの prefab 名）。</summary>
    public string Key => Content.Name;

    public DlcContentStatus Status { get; }

    /// <summary>取り除ける参照を全部取り除いても、次の保存でまだ要求されるか。</summary>
    public bool RemainsAfter { get; }

    public IReadOnlyList<DlcCategoryView> Categories { get; }

    /// <summary>mod が取り除ける参照の数。</summary>
    public int Removable => Categories.Where(c => DlcRemoval.IsRemovable(c.Action)).Sum(c => c.Count);

    /// <summary>取り除けない参照の数（一覧だけ・DLC が有効・付け替え先なし）。</summary>
    public int Blocked => Categories.Where(c => !DlcRemoval.IsRemovable(c.Action)).Sum(c => c.Count);

    /// <summary>
    /// 転出させる企業の数（確認文に出す）。企業 1 社は企業 prefab の参照（カテゴリ「企業」）と、ブランドの参照（カテゴリ「ブランド」）の
    /// 両方を持ちうるので、足さずに多い方を取る（同じ企業を二重に数えない）。ブランドが企業の参照と同じコンテンツしか要求しないときは、
    /// 走査がブランドの側を数えないので、ふつうは「企業」の件数そのものになる。
    /// </summary>
    public int CompaniesMovingAway => Math.Max(
        Categories.Where(c => c.Action == DlcAction.MoveAway && c.Category == DlcCategory.Company).Sum(c => c.Count),
        Categories.Where(c => c.Action == DlcAction.MoveAway && c.Category == DlcCategory.Brand).Sum(c => c.Count));
}

/// <summary>直前の取り除きの結果。</summary>
public sealed class DlcRemovalResult
{
    public DlcRemovalResult(string content)
    {
        Content = content;
    }

    /// <summary>取り除いたコンテンツのキー（空なら欠落している全コンテンツ）。</summary>
    public string Content { get; }

    /// <summary>Deleted を付けた Entity の数（車両は 1 両ずつ、付属物とその子・建物・置き物を含む。転出させた企業は含まない）。</summary>
    public int Deleted { get; set; }

    /// <summary>ブランドを付け替えた企業の数。</summary>
    public int Rebranded { get; set; }

    /// <summary>転出させた企業の数（企業 prefab が欠落した企業）。</summary>
    public int MovedAway { get; set; }

    /// <summary>車両指定を書き換えた路線の数。</summary>
    public int RoutesCleared { get; set; }

    /// <summary>
    /// 取り除けなかった参照の数（付け替え先なし・単独で消せない物・最上位の持ち主の prefab が欠落した付属物・
    /// 照会し直したら状態が変わっていた物）。
    /// </summary>
    public int Skipped { get; set; }

    /// <summary>
    /// 取り除きの後に新しく持ち主を失った付属物（とその子孫）を片付けた数。取り除きの前から持ち主の無かった物は数えない（触らない）。
    /// </summary>
    public int OrphansCleaned { get; set; }

    /// <summary>
    /// 取り除きが途中で例外になったか。それまでにコマンドバッファへ積んだ変更は反映されるので、件数はそこまでの分。
    /// </summary>
    public bool Failed { get; set; }
}

/// <summary>DLC タブへ渡す JSON を作る。</summary>
public static class DlcView
{
    public static string StatusKey(DlcContentStatus status) => status switch
    {
        DlcContentStatus.Missing => "missing",
        DlcContentStatus.Loaded => "loaded",
        _ => "mixed",
    };

    /// <param name="state">UI に見せる状態（idle / ready / removing）。</param>
    /// <param name="contents">走査の結果（まだ走査していなければ空）。</param>
    /// <param name="predicted">次の保存で要求されると予測されるコンテンツ名。</param>
    /// <param name="predictedAfter">取り除ける参照を全部取り除いた後でも要求されるコンテンツ名。</param>
    /// <param name="last">直前の取り除きの結果（まだなら null）。</param>
    public static string ToJson(
        string state,
        IReadOnlyList<DlcContentView> contents,
        IEnumerable<string> predicted,
        IEnumerable<string> predictedAfter,
        DlcRemovalResult? last)
    {
        var json = new JsonText().BeginObject();
        json.Key("state").Value(state);
        json.Key("last");
        if (last is null)
        {
            json.Value((string?)null);
        }
        else
        {
            json.BeginObject()
                .Key("content").Value(last.Content)
                .Key("deleted").Value(last.Deleted)
                .Key("rebranded").Value(last.Rebranded)
                .Key("movedAway").Value(last.MovedAway)
                .Key("routesCleared").Value(last.RoutesCleared)
                .Key("skipped").Value(last.Skipped)
                .Key("orphansCleaned").Value(last.OrphansCleaned)
                .Key("failed").Value(last.Failed)
                .EndObject();
        }
        json.Key("predicted").BeginArray();
        foreach (var name in predicted)
        {
            json.Value(name);
        }
        json.EndArray();
        json.Key("predictedAfter").BeginArray();
        foreach (var name in predictedAfter)
        {
            json.Value(name);
        }
        json.EndArray();
        json.Key("contents").BeginArray();
        foreach (var c in contents)
        {
            json.BeginObject()
                .Key("key").Value(c.Key)
                .Key("name").Value(c.Content.Name)
                .Key("dlcId").Value(c.Content.DlcId)
                .Key("status").Value(StatusKey(c.Status))
                // 一覧に出るのは次の保存で要求されると予測されたものだけなので、常に true（UI の表示を素直にするため明示する）。
                .Key("required").Value(true)
                .Key("remainsAfter").Value(c.RemainsAfter)
                .Key("removable").Value(c.Removable)
                .Key("blocked").Value(c.Blocked)
                .Key("companies").Value(c.CompaniesMovingAway)
                .Key("categories").BeginArray();
            foreach (var g in c.Categories)
            {
                json.BeginObject()
                    .Key("key").Value(g.Key)
                    .Key("kind").Value(DlcRemoval.CategoryKey(g.Category))
                    .Key("action").Value(DlcRemoval.ActionKey(g.Action))
                    .Key("count").Value(g.Count)
                    .Key("jumps").Value(g.Jumps.Count)
                    .Key("samples").BeginArray();
                foreach (var (name, count) in g.Targets
                    .OrderByDescending(t => t.Count)
                    .ThenBy(t => t.Name, StringComparer.Ordinal)
                    .Take(DlcDiagnostics.TargetNamesPerCategory))
                {
                    json.BeginObject().Key("name").Value(name).Key("count").Value(count).EndObject();
                }
                json.EndArray().EndObject();
            }
            json.EndArray().EndObject();
        }
        json.EndArray();
        return json.EndObject().ToString();
    }
}
