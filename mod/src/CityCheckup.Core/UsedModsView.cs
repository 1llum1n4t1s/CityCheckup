using System;
using System.Collections.Generic;
using System.Linq;

namespace CityCheckup.Core;

/// <summary>
/// 「MOD 使用の記録」（ゲームがセーブへ書く、読み込み時に有効だった mod の名前の集合）を UI へ渡す JSON にする。
/// </summary>
/// <remarks>
/// 集合そのもの（<c>CityConfigurationSystem.usedMods</c>）はゲーム側にあり、C# のシステムが名前を渡す。ここはゲーム型に依存しない。
/// </remarks>
public static class UsedModsView
{
    /// <summary>JSON に載せる名前の上限。パネルを長くしないため、残りは件数だけ（<c>more</c>）で伝える。</summary>
    public const int MaxNames = 8;

    /// <param name="names">記録されている mod の名前。</param>
    /// <param name="cleared">プレイヤーが記録を消した後か（消した後は別名で保存するよう案内する）。</param>
    /// <param name="clearedCount">消した記録の件数（消していなければ 0）。</param>
    public static string ToJson(IEnumerable<string?> names, bool cleared, int clearedCount)
    {
        var all = Normalize(names);
        var json = new JsonText().BeginObject();
        json.Key("count").Value(all.Count);
        json.Key("names").BeginArray();
        foreach (var name in all.Take(MaxNames))
        {
            json.Value(name);
        }
        json.EndArray();
        json.Key("more").Value(Math.Max(0, all.Count - MaxNames));
        json.Key("cleared").Value(cleared);
        json.Key("clearedCount").Value(clearedCount);
        return json.EndObject().ToString();
    }

    /// <summary>動作確認用のログの 1 行（件数と全部の名前）。</summary>
    public static string Describe(IEnumerable<string?> names)
    {
        var all = Normalize(names);
        return $"count={all.Count} [{string.Join(", ", all)}]";
    }

    // 空の名前は数えない。重複はゲーム側の集合が持たないが、渡し方に依存しないよう取り除いて並べる。
    private static List<string> Normalize(IEnumerable<string?> names) => names
        .Where(n => !string.IsNullOrWhiteSpace(n))
        .Select(n => n!)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(n => n, StringComparer.Ordinal)
        .ToList();
}
