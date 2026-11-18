// 画面の文言（日本語／英語）。C# 側 PanelUISystem の "locale"（ゲームの言語設定から決めた "ja" / "en"）で切り替える。
// 日本語以外のゲーム言語では英語を出す。所見（気づいたこと）の文は C# 側の LoadReport が同じ言語で作る。
import { api } from "./game";
import { GROUP, fmt } from "./style";

const locale$ = api.bindValue<string>(GROUP, "locale", "ja");

const ja = {
  toggle: "街の診断",
  toggleCollapsed: "街の診断（選択中は畳んでいます）",
  tabLoad: "負荷分析",
  tabMissing: "欠落アセット",
  tabDlc: "DLC",
  // 負荷分析
  findings: "気づいたこと",
  speedHeading: "シミュレーションの速度",
  selectedSpeed: "設定した速度",
  actualSpeed: "実際の速度",
  pathfindWait: "経路探し待ちで遅れた時間",
  simFrame: "1 フレームの処理時間",
  simFrameValue: (ms: number | null, budget: number | null) => `${fmt(ms, 1)} ms（上限 ${fmt(budget, 1)} ms）`,
  pending: "結果待ちの経路探し",
  count: (n: number | null, digits = 0) => `${fmt(n, digits)} 件`,
  fps: "画面のフレームレート",
  citizens: "人のデータ（観光客・通勤者を含む）",
  people: (n: number | null | undefined) => `${fmt(n)} 人`,
  pathfindHeading: "経路探しの作業量",
  counting10s: "数えています（10 秒ほどで表示します）…",
  counting: "数えています…",
  requestsPerFrame: "1 フレームあたりの依頼",
  workNote: "割合は探索した道の量（ゲームが記録する作業量）で、件数ではありません。",
  sourceValue: (share: string, perFrame: number | null, success: string) => `${share}（${fmt(perFrame, 1)} 件・成功 ${success}）`,
  sources: {
    jobSearch: "職探し",
    shopping: "市民の買い物",
    residentRepath: "歩行者の経路の引き直し",
    trips: "外出・レジャー",
    school: "学校探し",
    home: "住まい探し",
    serviceDispatch: "サービス車両の配車",
    vehicleRepath: "車両の経路の引き直し",
    other: "その他",
  } as Record<string, string>,
  jobsHeading: "職と失業",
  education: "学歴",
  seekers: "職を探す人",
  reachable: "就ける空き",
  levels: ["教育なし", "初等教育", "中等教育", "高等教育", "大学院"],
  reachableNote: (commuters: number) => `「就ける空き」は、その学歴以下で就ける空き職場の合計です。市外からの通勤者で職の無い人: ${fmt(commuters)} 人`,
  hotspotsHeading: "職の足りない地区",
  hotspot: (unemployed: number, vacancies: number) => `失業 ${fmt(unemployed)} 人 / 空き ${fmt(vacancies)}`,
  hotspotReach: (km: number | null) => (km === null ? "市内の空きを全部合わせても足りません" : `足りない職の半分が集まるのは約 ${fmt(km, 1)} km 先`),
  goSee: "見に行く",
  householdsHeading: "世帯",
  homeless: "ホームレスの世帯",
  movingAway: "転出中の世帯",
  scanTime: (ms: number | null | undefined) => `集計にかかった時間: ${fmt(ms, 0)} ms（10 秒ごと）`,
  // 欠落アセット
  missingHeading: "アンインストールした mod の物",
  scan: "探す",
  rescan: "探し直す",
  missingNote: "mod を外したあとも街に残っている物です。ゲームは代わりの見た目で表示しています。",
  deletedRefreshing: (n: number | null | undefined) => `${fmt(n)} 個を削除しました。一覧を更新しています…`,
  deletedJustNow: (n: number | null | undefined) => `直前に ${fmt(n)} 個を削除しました。`,
  notFound: "見つかりませんでした。",
  unknownName: "(名前不明)",
  kinds: { building: "建物", object: "置き物", net: "道路・線路", area: "エリア" } as Record<string, string>,
  groupDetail: (kind: string, count: number, deletable: number) =>
    `${kind}・${fmt(count)} か所` + (deletable < count ? `（消せるのは ${fmt(deletable)} か所。建物の一部や道路はここでは消せません）` : ""),
  confirmDelete: (n: number) => `本当に ${fmt(n)} 個を削除しますか？ 元に戻すには、保存せずに街を読み込み直してください。`,
  selectedCount: (n: number) => `${fmt(n)} 個を選んでいます。`,
  deleteNow: "削除する",
  deleteSelected: "選んだ物を削除",
  cancel: "やめる",
  // DLC
  dlcHeading: "セーブが必要とする DLC",
  dlcNote:
    "次に保存したとき、セーブを開くのに必要になる DLC と、その原因になっている物です。DLC を無効にして街を読み込むと、その DLC の物を取り除けます。",
  dlcNone: "なし",
  dlcAllMissing: "欠落している全コンテンツ",
  dlcPredicted: (names: string) => `次の保存で必要: ${names}`,
  dlcRemoving: "取り除いています。一覧を更新しています…",
  dlcDone: (content: string, deleted: number, rebranded: number, movedAway: number, routes: number, skipped: number, orphans: number) =>
    `${content}: ${fmt(deleted)} 個を削除、${fmt(rebranded)} 社のブランドを付け替え、${fmt(movedAway)} 社を転出させ、${fmt(routes)} 路線の車両指定を外しました。` +
    (orphans > 0 ? `持ち主を失って残った付属物 ${fmt(orphans)} 個も片付けました。` : "") +
    (skipped > 0 ? `取り除けなかった物: ${fmt(skipped)} 件。` : ""),
  dlcFailed: "途中で失敗しました（ここまでの変更は反映されます）。",
  dlcAdvanceBeforeSave: "後片付けはゲームの時間が進むと行われます。少し時間を進めてから保存してください。",
  usedModsHeading: "MOD 使用の記録",
  usedModsNote:
    "セーブには「MOD を使用しています」という印が付きます。消えるのは次の保存の印だけで、mod を有効にしたまま読み込み直すと、また付きます。mod の建物などをまだ使っているなら、その中身は残るので、先に「欠落アセット」や DLC の一覧で片付けてください。",
  usedModsNone: "記録されている mod はありません。",
  usedModsList: (count: number, names: string, more: number) => `${fmt(count)} 件: ${names}` + (more > 0 ? ` ほか ${fmt(more)} 件` : ""),
  usedModsClear: "記録を消す",
  usedModsCleared: (count: number) => `${fmt(count)} 件の記録を消しました。別名で保存してください。`,
  dlcStatus: {
    missing: "DLC は外れています。取り除けます。",
    loaded: "DLC が有効です。DLC を無効にして読み込み直すと取り除けます。",
    mixed: "一部は DLC が有効です。外れている分だけ取り除けます。",
  } as Record<string, string>,
  dlcRemainsAfter: "取り除いても、ここで取り除けない物が残るため、次の保存でまだ必要です。",
  dlcClearsAfter: "取り除くと、次の保存では必要なくなる見込みです。",
  dlcKinds: {
    vehicle: "車両",
    brand: "企業のブランド",
    subObject: "看板などの付属物",
    routeModel: "路線の車両指定",
    building: "建物・置き物",
    netArea: "道路・エリア（見に行くだけ）",
    company: "企業",
    system: "街の設定・統計の記録",
    other: "その他（未対応）",
  } as Record<string, string>,
  dlcCategoryLine: (kind: string, count: number) => `${kind}・${fmt(count)} 件`,
  dlcActions: {
    delete: "削除します",
    rebrand: "別のブランドに付け替えます",
    clearModel: "車両指定から外します",
    rebuildDelete: "削除して、持ち主に組み直させます",
    listOnly: "ここでは取り除けません",
    needsDlcDisabled: "DLC を無効にして読み込み直すと取り除けます",
    noCandidate: "付け替え先のブランドがありません",
    moveAway: "企業を転出させます（建物は空き物件になり、別の企業が入ります）",
  } as Record<string, string>,
  dlcRemove: "このコンテンツを取り除く",
  dlcRemoveNow: "取り除く",
  dlcConfirm: (deleting: number, rebranding: number, companies: number, blocked: number) =>
    `${fmt(deleting)} 件を取り除き、${fmt(rebranding)} 社のブランドを付け替えます。` +
    (companies > 0 ? `${fmt(companies)} 社を転出させます（従業員は職を失い、建物は空き物件になります）。` : "") +
    `元に戻せません（戻すには保存せずに読み込み直してください）。` +
    `車を消された世帯は、あとで別の車を持ちます。先に別名で保存しておくことをお勧めします。` +
    (blocked > 0 ? `ここで取り除けない ${fmt(blocked)} 件は残ります。` : ""),
  // コンテンツの表示名（ContentPrefab の名前 → 表示名）。無ければ元の名前を出す。
  contentNames: {
    BridgesAndPorts: "Bridges & Ports",
    SanFranciscoSet: "San Francisco Set",
    CS1TreasureHunt: "CS1 Treasure Hunt（特典）",
    LandmarkBuildings: "ランドマーク建物",
    Newsletter: "ニュースレター特典",
  } as Record<string, string>,
};

type Texts = typeof ja;

const en: Texts = {
  toggle: "City Checkup",
  toggleCollapsed: "City Checkup (hidden while something is selected)",
  tabLoad: "Load analysis",
  tabMissing: "Missing assets",
  tabDlc: "DLC",
  findings: "Findings",
  speedHeading: "Simulation speed",
  selectedSpeed: "Selected speed",
  actualSpeed: "Actual speed",
  pathfindWait: "Time slowed by pathfinding",
  simFrame: "Time per simulation frame",
  simFrameValue: (ms, budget) => `${fmt(ms, 1)} ms (limit ${fmt(budget, 1)} ms)`,
  pending: "Pathfinding requests waiting",
  count: (n, digits = 0) => `${fmt(n, digits)}`,
  fps: "Frame rate",
  citizens: "Citizen records (incl. tourists and commuters)",
  people: (n) => `${fmt(n)}`,
  pathfindHeading: "Pathfinding work",
  counting10s: "Counting (shown in about 10 seconds)…",
  counting: "Counting…",
  requestsPerFrame: "Requests per frame",
  workNote: "Shares are by search work recorded by the game, not by number of requests.",
  sourceValue: (share, perFrame, success) => `${share} (${fmt(perFrame, 1)}/frame, ${success} found)`,
  sources: {
    jobSearch: "Job search",
    shopping: "Shopping",
    residentRepath: "Pedestrian rerouting",
    trips: "Trips and leisure",
    school: "School search",
    home: "Home search",
    serviceDispatch: "Service vehicle dispatch",
    vehicleRepath: "Vehicle rerouting",
    other: "Other",
  },
  jobsHeading: "Jobs and unemployment",
  education: "Education",
  seekers: "Job seekers",
  reachable: "Open jobs",
  levels: ["Uneducated", "Elementary", "High school", "College", "University"],
  reachableNote: (commuters) =>
    `"Open jobs" counts every vacancy at or below that education level. Unemployed commuters from outside the city: ${fmt(commuters)}`,
  hotspotsHeading: "Districts short of jobs",
  hotspot: (unemployed, vacancies) => `${fmt(unemployed)} unemployed / ${fmt(vacancies)} open jobs`,
  hotspotReach: (km) => (km === null ? "All open jobs in the city are not enough" : `Half of the missing jobs are about ${fmt(km, 1)} km away`),
  goSee: "Go",
  householdsHeading: "Households",
  homeless: "Homeless households",
  movingAway: "Households moving away",
  scanTime: (ms) => `Counting took ${fmt(ms, 0)} ms (every 10 seconds)`,
  missingHeading: "Objects from uninstalled mods",
  scan: "Find",
  rescan: "Find again",
  missingNote: "These objects stayed in the city after their mod was removed. The game shows them with a placeholder.",
  deletedRefreshing: (n) => `Deleted ${fmt(n)}. Updating the list…`,
  deletedJustNow: (n) => `Deleted ${fmt(n)} just now.`,
  notFound: "Nothing found.",
  unknownName: "(unknown name)",
  kinds: { building: "Building", object: "Object", net: "Road / track", area: "Area" },
  groupDetail: (kind, count, deletable) =>
    `${kind}, ${fmt(count)} placed` + (deletable < count ? ` (${fmt(deletable)} can be deleted here; parts of buildings and roads cannot)` : ""),
  confirmDelete: (n) => `Delete ${fmt(n)} objects? To undo, load the city again without saving.`,
  selectedCount: (n) => `${fmt(n)} selected.`,
  deleteNow: "Delete",
  deleteSelected: "Delete selected",
  cancel: "Cancel",
  dlcHeading: "DLC this save needs",
  dlcNote:
    "The DLC that will be needed to open the save after the next save, and what causes it. Load the city with the DLC disabled to remove that DLC's items.",
  dlcNone: "none",
  dlcAllMissing: "All missing content",
  dlcPredicted: (names) => `Needed after the next save: ${names}`,
  dlcRemoving: "Removing. Updating the list…",
  dlcDone: (content, deleted, rebranded, movedAway, routes, skipped, orphans) =>
    `${content}: deleted ${fmt(deleted)}, rebranded ${fmt(rebranded)} companies, moved away ${fmt(movedAway)} companies, cleared vehicle choices on ${fmt(routes)} lines.` +
    (orphans > 0 ? ` Also cleaned up ${fmt(orphans)} attachments left without an owner.` : "") +
    (skipped > 0 ? ` Could not remove: ${fmt(skipped)}.` : ""),
  dlcFailed: "Removal stopped partway (the changes made so far still apply).",
  dlcAdvanceBeforeSave: "Cleanup happens as game time passes. Let the game run for a moment before saving.",
  usedModsHeading: "Mod usage record",
  usedModsNote:
    "Saves carry a \"uses mods\" mark. Only the mark of the next save is removed; it comes back if you load the city again with the mods enabled. Items from a mod stay in the city, so clean them up first with the Missing assets tab or the DLC list.",
  usedModsNone: "No mods are recorded.",
  usedModsList: (count, names, more) => `${fmt(count)}: ${names}` + (more > 0 ? ` and ${fmt(more)} more` : ""),
  usedModsClear: "Clear the record",
  usedModsCleared: (count) => `Cleared ${fmt(count)} records. Save under a new name.`,
  dlcStatus: {
    missing: "The DLC is not loaded. These can be removed.",
    loaded: "The DLC is enabled. Disable it and load the city again to remove these.",
    mixed: "Part of the DLC is enabled. Only the missing part can be removed.",
  },
  dlcRemainsAfter: "Some items cannot be removed here, so the next save will still need it.",
  dlcClearsAfter: "After removal, the next save should no longer need it.",
  dlcKinds: {
    vehicle: "Vehicles",
    brand: "Company brands",
    subObject: "Signs and other attachments",
    routeModel: "Line vehicle choices",
    building: "Buildings and objects",
    netArea: "Roads and areas (view only)",
    company: "Companies",
    system: "City settings and statistics records",
    other: "Other (not supported)",
  },
  dlcCategoryLine: (kind, count) => `${kind}: ${fmt(count)}`,
  dlcActions: {
    delete: "Will be deleted",
    rebrand: "Will be switched to another brand",
    clearModel: "Will be removed from the vehicle choice",
    rebuildDelete: "Will be deleted and rebuilt by the owner",
    listOnly: "Cannot be removed here",
    needsDlcDisabled: "Disable the DLC and load again to remove",
    noCandidate: "No other brand to switch to",
    moveAway: "The company will move away (the building becomes vacant for another company)",
  },
  dlcRemove: "Remove this content",
  dlcRemoveNow: "Remove",
  dlcConfirm: (deleting, rebranding, companies, blocked) =>
    `Remove ${fmt(deleting)} items and switch the brand of ${fmt(rebranding)} companies. ` +
    (companies > 0 ? `${fmt(companies)} companies will move away (their workers lose their jobs and the buildings become vacant). ` : "") +
    `This cannot be undone (load again without saving to undo). ` +
    `Households that lose a car will get another one later. Saving under a new name first is recommended.` +
    (blocked > 0 ? ` ${fmt(blocked)} items that cannot be removed here will stay.` : ""),
  contentNames: {
    BridgesAndPorts: "Bridges & Ports",
    SanFranciscoSet: "San Francisco Set",
    CS1TreasureHunt: "CS1 Treasure Hunt (bonus)",
    LandmarkBuildings: "Landmark Buildings",
    Newsletter: "Newsletter bonus",
  },
};

/** 今の表示言語の文言を返す。 */
export function useTexts(): Texts {
  const locale = api.useValue(locale$);
  return locale === "ja" ? ja : en;
}
