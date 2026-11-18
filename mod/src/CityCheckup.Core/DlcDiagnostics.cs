using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CityCheckup.Core;

/// <summary>ゲームの Entity を Core 側で預かるための Index と Version。</summary>
public readonly struct EntityId : IEquatable<EntityId>
{
    public EntityId(int index, int version)
    {
        Index = index;
        Version = version;
    }

    public int Index { get; }

    public int Version { get; }

    public bool Equals(EntityId other) => Index == other.Index && Version == other.Version;

    public override bool Equals(object? obj) => obj is EntityId other && Equals(other);

    public override int GetHashCode() => (Index * 397) ^ Version;

    public override string ToString() => Index.ToString(CultureInfo.InvariantCulture) + ":" + Version.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// 保存時にゲームが「街から参照された prefab」へ印を付ける経路（PrimaryPrefabReferencesSystem の照会）。
/// </summary>
public enum DlcRefPath
{
    /// <summary>PrefabRef（建物・物・道路区間など、街のほとんどの Entity）。</summary>
    PrefabRef,

    /// <summary>UnderConstruction.m_NewPrefab（建設中・成長中の建物の次の prefab）。</summary>
    UnderConstruction,

    /// <summary>CompanyData.m_Brand。</summary>
    CompanyBrand,

    /// <summary>Policy バッファの m_Policy。</summary>
    Policy,

    /// <summary>ServiceBudgetData バッファの m_Service（除外条件なし）。</summary>
    ServiceBudget,

    /// <summary>VehicleModel バッファの m_PrimaryPrefab。</summary>
    VehicleModelPrimary,

    /// <summary>VehicleModel バッファの m_SecondaryPrefab。</summary>
    VehicleModelSecondary,

    /// <summary>Game.Tools.EditorContainer.m_Prefab。</summary>
    EditorContainer,

    /// <summary>Game.Triggers.Chirp.m_Sender（PrefabData を持つ場合だけ）。</summary>
    ChirpSender,

    /// <summary>ChirpEntity バッファの m_Entity（PrefabData を持つ場合だけ）。</summary>
    ChirpEntity,

    /// <summary>SubReplacement バッファの m_Prefab。</summary>
    SubReplacement,

    // ここから下は Entity ではなくシステムが持つ参照（PrimaryPrefabReferencesSystem.PatchSystemReferences）。
    // 1 回目の CheckPrefabReferencesSystem より前に印が付くので、ContentPrerequisiteData の伝播にも乗る。

    /// <summary>CityConfigurationSystem.defaultTheme / loadedDefaultTheme（街のテーマ）。</summary>
    CityTheme,

    /// <summary>CityConfigurationSystem.requiredContent（マップが要求するコンテンツ。ContentPrefab そのもの）。</summary>
    CityRequiredContent,

    /// <summary>ClimateSystem.currentClimate（気候）。</summary>
    Climate,

    /// <summary>TerrainMaterialSystem.currentTerrainRenderSettings（地形の描画設定）。</summary>
    TerrainRenderSettings,

    /// <summary>WaterRenderSystem.currentWaterRenderSettings（水の描画設定）。</summary>
    WaterRenderSettings,

    /// <summary>TransportUsageTrackSystem の建物 prefab ごとの輸送の記録（キーが建物 prefab）。</summary>
    TransportUsage,
}

/// <summary>参照された prefab からコンテンツ（DLC など）へつながる経路。</summary>
public enum DlcReach
{
    /// <summary>参照された prefab が ContentPrefab そのもの。</summary>
    Direct,

    /// <summary>参照された prefab が ContentPrerequisiteData で指している。</summary>
    ContentPrerequisite,

    /// <summary>参照された prefab が ModPrerequisiteData で指している。</summary>
    ModPrerequisite,
}

/// <summary>ContentData を持つ prefab（DLC などのコンテンツ）。</summary>
public sealed class DlcContent
{
    public DlcContent(EntityId id, string name, int dlcId, uint flags, bool missing, bool hasContentData = true)
    {
        Id = id;
        Name = name;
        DlcId = dlcId;
        Flags = flags;
        Missing = missing;
        HasContentData = hasContentData;
    }

    public EntityId Id { get; }

    /// <summary>prefab の名前。欠落しているときは記録された元の名前（obsolete ID）。</summary>
    public string Name { get; }

    public int DlcId { get; }

    public uint Flags { get; }

    /// <summary>prefab が欠落（PrefabData が無効、または obsolete）しているか。</summary>
    public bool Missing { get; }

    /// <summary>ContentData を持っているか。前提として指されているのに持たない prefab を見分けるため。</summary>
    public bool HasContentData { get; }
}

/// <summary>ContentPrerequisiteData または ModPrerequisiteData でコンテンツを指している prefab。</summary>
public readonly struct DlcPrerequisite
{
    public DlcPrerequisite(EntityId holder, string holderName, EntityId content, string contentName, DlcReach kind, bool holderEnabled)
    {
        Holder = holder;
        HolderName = holderName;
        Content = content;
        ContentName = contentName;
        Kind = kind;
        HolderEnabled = holderEnabled;
    }

    public EntityId Holder { get; }

    public string HolderName { get; }

    public EntityId Content { get; }

    /// <summary>ContentData が無い prefab を指していたときに、その名前を出すため。</summary>
    public string ContentName { get; }

    /// <summary>ContentPrerequisite か ModPrerequisite（Direct は使わない）。</summary>
    public DlcReach Kind { get; }

    /// <summary>保有 prefab の PrefabData が有効か。</summary>
    public bool HolderEnabled { get; }
}

/// <summary>参照の見本 1 件。</summary>
public readonly struct DlcReferenceSample
{
    public DlcReferenceSample(EntityId holder, string holderPrefabName, bool hasPosition, float x, float y, float z)
    {
        Holder = holder;
        HolderPrefabName = holderPrefabName;
        HasPosition = hasPosition;
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>参照を持っていた Entity。</summary>
    public EntityId Holder { get; }

    /// <summary>その Entity の prefab 名（prefab 自身が持つ参照のときは空）。</summary>
    public string HolderPrefabName { get; }

    public bool HasPosition { get; }

    public float X { get; }

    public float Y { get; }

    public float Z { get; }
}

/// <summary>
/// セーブがどの DLC を要求するかの診断（読み取り専用）。街の参照をたどり、「次に保存したら要求されるコンテンツ」を予測する。
/// </summary>
/// <remarks>
/// ゲームは保存時に、街から参照される prefab へ印を付け（PrimaryPrefabReferencesSystem）、その prefab の
/// ContentPrerequisiteData / ModPrerequisiteData が指すコンテンツへさらに印を付ける（SecondaryPrefabReferencesSystem）。
/// 印の付いたコンテンツがセーブの contentPrerequisites になる。ここではその規則を、ゲームの型を使わずに再現して集計する。
/// 再現している範囲: Primary の照会（Entity の参照）とシステムが持つ参照（PatchSystemReferences のうち公開されているもの。
/// ClimateSystem の天気の効果は非公開なので読まない）、Secondary のうち ContentPrerequisiteData / ModPrerequisiteData の伝播。
/// Secondary のほかの伝播（成長型建物の区画、代替建物、サービス、レーン、路線の prefab 間のつながり）はたどらない。
/// 参照の数は桁違いに多いので、コンテンツへつながる prefab（関係する prefab）への参照だけを数え、見本は少数に絞る。
/// </remarks>
public sealed class DlcDiagnostics
{
    /// <summary>1 つの（経路, 参照先 prefab）あたりに残す見本の数。</summary>
    public const int SamplesPerTarget = 3;

    /// <summary>1 つの出力項目（コンテンツ, 到達経路, 参照経路）に載せる見本の数。</summary>
    public const int SamplesPerGroup = 3;

    /// <summary>コンテンツごとの前提を持つ prefab の名前を並べる数。</summary>
    public const int HolderNamesPerContent = 5;

    /// <summary>1 つのカテゴリで「見に行く」先として残す数。全件だと数万件になり、JSON にも載せない。</summary>
    public const int JumpsPerCategory = 200;

    /// <summary>1 つのカテゴリで UI に名前を出す参照先 prefab の数。</summary>
    public const int TargetNamesPerCategory = 3;

    private static readonly (uint Bit, string Name)[] KnownFlags =
    {
        (1u, "RequireDlc"),
        (2u, "RequirePdxLogin"),
        (4u, "RequireMod"),
    };

    private static readonly (int Id, string Name)[] KnownDlcIds =
    {
        (0, "LandmarkBuildings"),
        (1, "SanFranciscoSet"),
        (2, "CS1TreasureHunt"),
        (5, "BridgesAndPorts"),
        (1010, "Newsletter"),
    };

    private readonly Dictionary<EntityId, DlcContent> m_Contents = new();
    private readonly List<DlcPrerequisite> m_Prerequisites = new();

    // 前提を持つ prefab → 指しているコンテンツ（ブランドの候補選びで prefab ごとに引くため）。
    private readonly Dictionary<EntityId, HashSet<EntityId>> m_RequiredByHolder = new();

    // 参照先 prefab → そこからつながるコンテンツと経路。
    private readonly Dictionary<EntityId, List<(EntityId Content, DlcReach Reach)>> m_Targets = new();

    // 参照先 prefab の名前（見本の表示用）。
    private readonly Dictionary<EntityId, string> m_TargetNames = new();

    private readonly Dictionary<(DlcRefPath Path, EntityId Target), Tally> m_Tallies = new();

    // 参照先 prefab × 分類ごとの件数と「見に行く」先（DLC タブのカテゴリはこれをコンテンツごとに束ねる）。
    private readonly Dictionary<(EntityId Target, DlcClass Class), Tally> m_Classes = new();

    // 経路 × 保有 Entity の種類 × 参照先が欠落しているか、ごとの件数（診断ログの内訳）。
    private readonly Dictionary<(DlcRefPath Path, DlcHolderKind Kind, bool Missing), int> m_KindTallies = new();

    public DlcDiagnostics(IEnumerable<DlcContent> contents, IEnumerable<DlcPrerequisite> prerequisites)
    {
        foreach (var c in contents)
        {
            m_Contents[c.Id] = c;
            AddReach(c.Id, c.Id, DlcReach.Direct, c.Name);
        }
        foreach (var p in prerequisites)
        {
            m_Prerequisites.Add(p);
            if (!m_RequiredByHolder.TryGetValue(p.Holder, out var required))
            {
                required = new HashSet<EntityId>();
                m_RequiredByHolder[p.Holder] = required;
            }
            required.Add(p.Content);
            if (!m_Contents.ContainsKey(p.Content))
            {
                // ContentData を持たない prefab が前提として指されている。保存時の印付けは到達するが、
                // 保存される contentPrerequisites には出ない（ContentData が条件）ので、区別して表示する。
                m_Contents[p.Content] = new DlcContent(p.Content, p.ContentName, -1, 0, missing: true, hasContentData: false);
            }
            AddReach(p.Holder, p.Content, p.Kind, p.HolderName);
        }
    }

    public IReadOnlyCollection<DlcContent> Contents => m_Contents.Values;

    /// <summary>この prefab が参照されたとき、コンテンツへつながるか（数える価値があるか）。</summary>
    public bool IsRelevant(EntityId target) => m_Targets.ContainsKey(target);

    /// <summary>
    /// prefab への参照を 1 件数える（分類なし）。関係しない prefab は無視する。
    /// </summary>
    /// <returns>診断の見本がまだ足りないとき true。その場合は <see cref="AddSample(DlcRefPath, EntityId, DlcReferenceSample)"/> で見本を渡す。</returns>
    public bool Record(DlcRefPath path, EntityId target)
    {
        var (diag, _) = RecordCore(path, target, default, classified: false);
        return diag;
    }

    /// <summary>
    /// prefab への参照を分類つきで 1 件数える。関係しない prefab は無視する。
    /// </summary>
    /// <returns>
    /// 診断の見本か、カテゴリの「見に行く」先がまだ足りないとき true。その場合は
    /// <see cref="AddSample(DlcRefPath, EntityId, DlcClass, DlcReferenceSample, DlcReferenceSample)"/> で見本を渡す。
    /// </returns>
    public bool Record(DlcRefPath path, EntityId target, DlcClass cls)
    {
        var (diag, jump) = RecordCore(path, target, cls, classified: true);
        return diag || jump;
    }

    public void AddSample(DlcRefPath path, EntityId target, DlcReferenceSample sample)
    {
        if (m_Tallies.TryGetValue((path, target), out var tally) && tally.Samples.Count < SamplesPerTarget)
        {
            tally.Samples.Add(sample);
        }
    }

    /// <param name="sample">診断ログ用の見本（参照を持つ Entity）。</param>
    /// <param name="jump">「見に行く」で選ぶ先（企業なら入居している建物など、参照を持つ Entity と違うことがある）。</param>
    public void AddSample(DlcRefPath path, EntityId target, DlcClass cls, DlcReferenceSample sample, DlcReferenceSample jump)
    {
        AddSample(path, target, sample);
        if (m_Classes.TryGetValue((target, cls), out var tally) && tally.Samples.Count < JumpsPerCategory)
        {
            tally.Samples.Add(jump);
        }
    }

    /// <param name="classified">分類つきの記録か。分類なしの記録は経路ごとの件数（診断ログ）にだけ数え、カテゴリ・内訳・取り除きには使わない。</param>
    private (bool Diag, bool Jump) RecordCore(DlcRefPath path, EntityId target, DlcClass cls, bool classified)
    {
        if (!m_Targets.ContainsKey(target))
        {
            return (false, false);
        }
        var key = (path, target);
        if (!m_Tallies.TryGetValue(key, out var tally))
        {
            tally = new Tally();
            m_Tallies[key] = tally;
        }
        tally.Count++;
        if (!classified)
        {
            return (tally.Samples.Count < SamplesPerTarget, false);
        }

        if (!m_Classes.TryGetValue((target, cls), out var byClass))
        {
            byClass = new Tally();
            m_Classes[(target, cls)] = byClass;
        }
        byClass.Count++;

        var kindKey = (path, cls.Kind, cls.TargetMissing);
        m_KindTallies[kindKey] = m_KindTallies.TryGetValue(kindKey, out var n) ? n + 1 : 1;
        return (tally.Samples.Count < SamplesPerTarget, byClass.Samples.Count < JumpsPerCategory);
    }

    /// <summary>この prefab が ContentPrerequisiteData / ModPrerequisiteData で指すコンテンツ（ブランドの候補選びに使う）。</summary>
    public IReadOnlyCollection<EntityId> RequiredContentsOf(EntityId prefab) =>
        m_RequiredByHolder.TryGetValue(prefab, out var required) ? required : (IReadOnlyCollection<EntityId>)Array.Empty<EntityId>();

    /// <summary>欠落しているコンテンツ（DLC を外したもの・所有していないもの）。</summary>
    public IReadOnlyCollection<EntityId> MissingContents() =>
        m_Contents.Values.Where(c => c.Missing && c.HasContentData).Select(c => c.Id).ToList();

    /// <summary>
    /// コンテンツ（キーは prefab 名）へつながる参照先のうち、欠落している prefab。取り除きの処理はこれを参照する Entity を照会し直す。
    /// </summary>
    public IReadOnlyCollection<EntityId> RemovalTargets(string contentKey)
    {
        var content = FindContent(contentKey);
        var result = new HashSet<EntityId>();
        if (content is null)
        {
            return result;
        }
        foreach (var ((target, cls), tally) in m_Classes)
        {
            if (tally.Count > 0 && cls.TargetMissing && Reaches(target, content.Id))
            {
                result.Add(target);
            }
        }
        return result;
    }

    /// <summary>キー（prefab 名）でコンテンツを引く。ContentData を持つものだけ。</summary>
    public DlcContent? FindContent(string contentKey) =>
        m_Contents.Values.FirstOrDefault(c => c.HasContentData && string.Equals(c.Name, contentKey, StringComparison.Ordinal));

    /// <summary>
    /// <paramref name="owner"/> の参照先がつながるコンテンツが、<paramref name="target"/> のつながるコンテンツをすべて含むか。
    /// 含むなら、持ち主を取り除けば付属物の分の要求も一緒に消えるので、付属物を別に数えなくてよい。
    /// </summary>
    public bool Covers(EntityId owner, EntityId target)
    {
        if (!m_Targets.TryGetValue(target, out var reached) || !m_Targets.TryGetValue(owner, out var covering))
        {
            return false;
        }
        return reached.All(r => covering.Any(c => c.Content.Equals(r.Content)));
    }

    private bool Reaches(EntityId target, EntityId content) =>
        m_Targets.TryGetValue(target, out var list) && list.Any(r => r.Content.Equals(content));

    /// <summary>
    /// 次に保存したら contentPrerequisites に出ると予測されるコンテンツ。
    /// 印の付く条件（参照された prefab から到達する）に加え、保存される条件（ContentData を持つ）も満たすものだけ。
    /// </summary>
    public IReadOnlyList<DlcContent> PredictedContents()
    {
        var reached = new HashSet<EntityId>();
        foreach (var ((_, target), tally) in m_Tallies)
        {
            if (tally.Count == 0)
            {
                continue;
            }
            foreach (var (content, _) in m_Targets[target])
            {
                reached.Add(content);
            }
        }
        return reached
            .Select(id => m_Contents[id])
            .Where(c => c.HasContentData)
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// mod が取り除ける参照（<see cref="DlcRemoval.IsRemovable"/>）を全部取り除いた後でも、次の保存で要求されると予測されるコンテンツ。
    /// 一覧だけ・DLC が有効なまま・付け替え先なしの参照が残るコンテンツがここに出る。
    /// </summary>
    public IReadOnlyList<DlcContent> PredictedAfterRemoval()
    {
        var reached = new HashSet<EntityId>();
        foreach (var ((target, cls), tally) in m_Classes)
        {
            if (tally.Count == 0 || DlcRemoval.IsRemovable(cls.Action))
            {
                continue;
            }
            foreach (var (content, _) in m_Targets[target])
            {
                reached.Add(content);
            }
        }
        return reached
            .Select(id => m_Contents[id])
            .Where(c => c.HasContentData)
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// DLC タブの表示単位（コンテンツ → カテゴリ）にまとめる。次の保存で要求されると予測されるコンテンツだけを並べる。
    /// </summary>
    public IReadOnlyList<DlcContentView> BuildView()
    {
        var after = new HashSet<EntityId>(PredictedAfterRemoval().Select(c => c.Id));
        var result = new List<DlcContentView>();
        foreach (var content in PredictedContents())
        {
            var categories = new Dictionary<(DlcCategory, DlcAction), DlcCategoryView>();
            var missing = 0;
            var loaded = 0;
            foreach (var ((target, cls), tally) in m_Classes
                .Where(e => e.Value.Count > 0 && Reaches(e.Key.Target, content.Id))
                .OrderByDescending(e => e.Value.Count)
                .ThenBy(e => m_TargetNames[e.Key.Target], StringComparer.Ordinal))
            {
                if (cls.TargetMissing)
                {
                    missing += tally.Count;
                }
                else
                {
                    loaded += tally.Count;
                }
                var key = (cls.Category, cls.Action);
                if (!categories.TryGetValue(key, out var view))
                {
                    view = new DlcCategoryView(
                        $"{content.Name}|{DlcRemoval.CategoryKey(cls.Category)}|{DlcRemoval.ActionKey(cls.Action)}",
                        cls.Category,
                        cls.Action);
                    categories[key] = view;
                }
                view.Add(m_TargetNames[target], tally.Count, tally.Samples);
            }
            var status = missing > 0 && loaded > 0 ? DlcContentStatus.Mixed
                : missing > 0 ? DlcContentStatus.Missing
                : loaded > 0 ? DlcContentStatus.Loaded
                : content.Missing ? DlcContentStatus.Missing : DlcContentStatus.Loaded;
            result.Add(new DlcContentView(
                content,
                status,
                after.Contains(content.Id),
                categories.Values.OrderBy(c => c.Category).ThenBy(c => c.Action).ToList()));
        }
        return result;
    }

    /// <summary>
    /// 診断結果を Player.log 向けの行にする。
    /// </summary>
    /// <param name="saveContent">読み込んだセーブのメタデータの contentPrerequisites（取れなければ null）。</param>
    /// <param name="saveContentNote">saveContent が null のときの理由。</param>
    public IReadOnlyList<string> BuildLines(IReadOnlyList<string>? saveContent, string? saveContentNote)
    {
        var lines = new List<string>
        {
            $"dlc diagnostic begin: contents={m_Contents.Count(c => c.Value.HasContentData)} prerequisiteLinks={m_Prerequisites.Count} relevantPrefabs={m_Targets.Count}",
        };

        foreach (var content in m_Contents.Values.OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            lines.Add(ContentLine(content));
            AppendHolders(lines, content);
            AppendReferences(lines, content);
            AppendCategories(lines, content);
        }

        AppendKinds(lines);
        var predicted = PredictedContents();
        lines.Add($"dlc predicted (a save now would require): {JoinNames(predicted.Select(c => c.Name))}");
        lines.Add($"dlc predicted after removal (would still require): {JoinNames(PredictedAfterRemoval().Select(c => c.Name))}");
        if (saveContent is null)
        {
            lines.Add($"dlc save metadata contentPrerequisites: unavailable ({saveContentNote ?? "reason unknown"})");
        }
        else
        {
            lines.Add($"dlc save metadata contentPrerequisites: {JoinNames(saveContent)}");
        }
        lines.Add("dlc diagnostic end");
        return lines;
    }

    /// <summary>ContentFlags の値を名前にする（未知のビットは 16 進で足す）。</summary>
    public static string FormatFlags(uint flags)
    {
        if (flags == 0)
        {
            return "None";
        }
        var parts = new List<string>();
        var rest = flags;
        foreach (var (bit, name) in KnownFlags)
        {
            if ((rest & bit) != 0)
            {
                parts.Add(name);
                rest &= ~bit;
            }
        }
        if (rest != 0)
        {
            parts.Add("0x" + rest.ToString("X", CultureInfo.InvariantCulture));
        }
        return string.Join("|", parts);
    }

    /// <summary>DLC の ID を「5(BridgesAndPorts)」の形にする。名前が分からない ID は数字だけ。</summary>
    public static string FormatDlcId(int dlcId)
    {
        foreach (var (id, name) in KnownDlcIds)
        {
            if (id == dlcId)
            {
                return $"{dlcId}({name})";
            }
        }
        return dlcId.ToString(CultureInfo.InvariantCulture);
    }

    private static string JoinNames(IEnumerable<string> names)
    {
        var list = names.ToList();
        return list.Count == 0 ? "(none)" : string.Join(", ", list);
    }

    private void AddReach(EntityId target, EntityId content, DlcReach reach, string targetName)
    {
        if (!m_Targets.TryGetValue(target, out var list))
        {
            list = new List<(EntityId, DlcReach)>();
            m_Targets[target] = list;
        }
        if (!list.Contains((content, reach)))
        {
            list.Add((content, reach));
        }
        m_TargetNames[target] = targetName;
    }

    private string ContentLine(DlcContent c)
    {
        var holders = m_Prerequisites.Where(p => p.Content.Equals(c.Id)).ToList();
        var enabled = holders.Count(p => p.HolderEnabled);
        var byContent = holders.Count(p => p.Kind == DlcReach.ContentPrerequisite);
        var byMod = holders.Count(p => p.Kind == DlcReach.ModPrerequisite);
        var data = c.HasContentData
            ? $"dlcId={FormatDlcId(c.DlcId)} flags={FormatFlags(c.Flags)}"
            : "dlcId=- flags=- (no ContentData)";
        return $"dlc content '{c.Name}' entity={c.Id} {data} missing={(c.Missing ? "yes" : "no")}"
            + $" prerequisiteHolders={holders.Count} (content={byContent} mod={byMod}; prefabData enabled={enabled} disabled={holders.Count - enabled})";
    }

    private void AppendHolders(List<string> lines, DlcContent c)
    {
        var names = m_Prerequisites
            .Where(p => p.Content.Equals(c.Id))
            .Select(p => p.HolderName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .Take(HolderNamesPerContent)
            .ToList();
        if (names.Count > 0)
        {
            lines.Add($"dlc   holders (first {names.Count}): {string.Join(", ", names)}");
        }
    }

    private void AppendReferences(List<string> lines, DlcContent c)
    {
        // (到達経路, 参照経路) ごとに、このコンテンツへつながる参照先 prefab の集計を集める。
        var groups = new Dictionary<(DlcReach Reach, DlcRefPath Path), List<(EntityId Target, Tally Tally)>>();
        foreach (var ((path, target), tally) in m_Tallies)
        {
            foreach (var (content, reach) in m_Targets[target])
            {
                if (!content.Equals(c.Id))
                {
                    continue;
                }
                var key = (reach, path);
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<(EntityId, Tally)>();
                    groups[key] = list;
                }
                list.Add((target, tally));
            }
        }
        if (groups.Count == 0)
        {
            lines.Add("dlc   references: none");
            return;
        }
        foreach (var ((reach, path), list) in groups.OrderBy(g => g.Key.Reach).ThenBy(g => g.Key.Path))
        {
            var count = list.Sum(t => t.Tally.Count);
            lines.Add($"dlc   via={reach} path={path} refs={count} targetPrefabs={list.Count}");
            var shown = 0;
            foreach (var (target, tally) in list.OrderByDescending(t => t.Tally.Count))
            {
                foreach (var s in tally.Samples)
                {
                    if (shown++ >= SamplesPerGroup)
                    {
                        break;
                    }
                    lines.Add("dlc     sample " + SampleText(s, m_TargetNames[target]));
                }
            }
        }
    }

    /// <summary>コンテンツごとの「カテゴリ/処置=件数」（参照先が欠落しているものと有効なものを分けて）。</summary>
    private void AppendCategories(List<string> lines, DlcContent c)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var ((target, cls), tally) in m_Classes)
        {
            if (tally.Count == 0 || !Reaches(target, c.Id))
            {
                continue;
            }
            var key = $"{DlcRemoval.CategoryKey(cls.Category)}/{DlcRemoval.ActionKey(cls.Action)}";
            counts[key] = counts.TryGetValue(key, out var n) ? n + tally.Count : tally.Count;
        }
        if (counts.Count > 0)
        {
            lines.Add("dlc   categories: " + string.Join(" ", counts.Select(kv => $"{kv.Key}={kv.Value.ToString(CultureInfo.InvariantCulture)}")));
        }
    }

    /// <summary>経路ごとの保有 Entity の種類の内訳（参照先が欠落している件数つき）。分類なしの記録は出さない。</summary>
    private void AppendKinds(List<string> lines)
    {
        foreach (var path in m_KindTallies.Keys.Select(k => k.Path).Distinct().OrderBy(p => p))
        {
            var parts = new List<string>();
            foreach (var kind in m_KindTallies.Keys.Where(k => k.Path == path).Select(k => k.Kind).Distinct().OrderBy(k => k))
            {
                m_KindTallies.TryGetValue((path, kind, true), out var missing);
                m_KindTallies.TryGetValue((path, kind, false), out var loaded);
                parts.Add($"{DlcRemoval.KindKey(kind)}={(missing + loaded).ToString(CultureInfo.InvariantCulture)}(missing {missing.ToString(CultureInfo.InvariantCulture)})");
            }
            lines.Add($"dlc holder kinds path={path}: {string.Join(" ", parts)}");
        }
    }

    private static string SampleText(DlcReferenceSample s, string targetName)
    {
        var sb = new StringBuilder();
        sb.Append("entity=").Append(s.Holder);
        sb.Append(" holderPrefab='").Append(s.HolderPrefabName.Length == 0 ? "(prefab itself)" : s.HolderPrefabName).Append('\'');
        sb.Append(" target='").Append(targetName).Append('\'');
        if (s.HasPosition)
        {
            sb.Append(" pos=(")
                .Append(s.X.ToString("0.0", CultureInfo.InvariantCulture)).Append(", ")
                .Append(s.Y.ToString("0.0", CultureInfo.InvariantCulture)).Append(", ")
                .Append(s.Z.ToString("0.0", CultureInfo.InvariantCulture)).Append(')');
        }
        return sb.ToString();
    }

    private sealed class Tally
    {
        public int Count { get; set; }

        public List<DlcReferenceSample> Samples { get; } = new();
    }
}
