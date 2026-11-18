// 「DLC」タブ。C# 側 DlcRemovalSystem の "dlc"（Core の DlcView.ToJson の JSON 文字列）を表示し、
// 探す・見に行く・コンテンツを取り除くを C# 側のトリガー（scanDlc / jumpDlc / removeDlc）へ送る。
import { React, api } from "./game";
import { useTexts } from "./i18n";
import { GROUP, colors, fmt, s } from "./style";

const dlc$ = api.bindValue<string>(GROUP, "dlc", "{}");

export type DlcAction = "delete" | "rebrand" | "clearModel" | "rebuildDelete" | "listOnly" | "needsDlcDisabled" | "noCandidate" | "moveAway";

interface DlcCategory {
  key: string;
  kind: string;
  action: DlcAction;
  count: number;
  jumps: number;
  samples: { name: string; count: number }[];
}

interface DlcContent {
  key: string;
  name: string;
  dlcId: number;
  status: "missing" | "loaded" | "mixed";
  required: boolean;
  remainsAfter: boolean;
  removable: number;
  blocked: number;
  companies: number;
  categories: DlcCategory[];
}

interface DlcReport {
  state?: "idle" | "ready" | "removing";
  last?: {
    content: string;
    deleted: number;
    rebranded: number;
    movedAway?: number;
    routesCleared: number;
    skipped: number;
    orphansCleaned?: number;
    failed?: boolean;
  } | null;
  predicted?: string[];
  predictedAfter?: string[];
  contents?: DlcContent[];
}

function parse(text: string): DlcReport {
  try {
    return JSON.parse(text) as DlcReport;
  } catch {
    return {};
  }
}

// mod が取り除ける処置（C# 側 DlcRemoval.IsRemovable と同じ）。
const removable = (a: DlcAction) => a === "delete" || a === "rebrand" || a === "clearModel" || a === "rebuildDelete" || a === "moveAway";

export function DlcTab() {
  const t = useTexts();
  const text = api.useValue(dlc$);
  const report = React.useMemo(() => parse(text), [text]);
  const contents = report.contents ?? [];
  const predicted = report.predicted ?? [];
  // 「見に行く」で次に見る順番（カテゴリの key ごと）。
  const [cursor, setCursor] = React.useState<Record<string, number>>({});
  // 取り除きは 2 段階（1 回目で確認表示、2 回目で実行）にして押し間違いを防ぐ。確認中のコンテンツの key。
  const [confirming, setConfirming] = React.useState<string | null>(null);

  const name = (key: string) => t.contentNames[key] ?? key;
  const names = (keys: string[]) => (keys.length === 0 ? t.dlcNone : keys.map(name).join(", "));

  const jump = (c: DlcCategory) => {
    const i = cursor[c.key] ?? 0;
    api.trigger(GROUP, "jumpDlc", c.key, i);
    setCursor({ ...cursor, [c.key]: (i + 1) % Math.max(1, c.jumps) });
  };

  const remove = (c: DlcContent) => {
    if (confirming !== c.key) {
      setConfirming(c.key);
      return;
    }
    api.trigger(GROUP, "removeDlc", c.key);
    setConfirming(null);
  };

  return (
    <div>
      <div style={s.row}>
        <div style={s.heading}>{t.dlcHeading}</div>
        <button style={s.smallButton} onClick={() => api.trigger(GROUP, "scanDlc")}>
          {report.state === "idle" || report.state === undefined ? t.scan : t.rescan}
        </button>
      </div>
      <div style={s.sub}>{t.dlcNote}</div>

      {report.state === "removing" && <div style={{ marginTop: "6rem" }}>{t.dlcRemoving}</div>}
      {report.state === "ready" && report.last && (
        <div style={{ ...s.section, color: colors.warn }}>
          {report.last.failed && <div style={{ marginBottom: "4rem" }}>{t.dlcFailed}</div>}
          <div>
            {t.dlcDone(
              name(report.last.content || t.dlcAllMissing),
              report.last.deleted,
              report.last.rebranded,
              report.last.movedAway ?? 0,
              report.last.routesCleared,
              report.last.skipped,
              report.last.orphansCleaned ?? 0,
            )}
          </div>
          <div style={{ marginTop: "4rem" }}>{t.dlcAdvanceBeforeSave}</div>
        </div>
      )}
      {report.state !== undefined && report.state !== "idle" && (
        <div style={{ marginTop: "6rem" }}>{t.dlcPredicted(names(predicted))}</div>
      )}
      {report.state === "ready" && contents.length === 0 && <div style={{ marginTop: "8rem" }}>{t.notFound}</div>}

      {contents.map((c) => {
        // 転出させる企業は件数を企業数（C# の DlcContentView.CompaniesMovingAway）で別に出すので、削除の件数に入れない。
        const deleting = c.categories
          .filter((g) => removable(g.action) && g.action !== "rebrand" && g.action !== "moveAway")
          .reduce((n, g) => n + g.count, 0);
        const rebranding = c.categories.filter((g) => g.action === "rebrand").reduce((n, g) => n + g.count, 0);
        const companies = c.companies ?? 0;
        const canRemove = c.removable > 0 && c.status !== "loaded";
        const isConfirming = confirming === c.key;
        return (
          <div key={c.key} style={s.section}>
            <div style={s.heading}>{name(c.key)}</div>
            <div style={s.sub}>{t.dlcStatus[c.status] ?? c.status}</div>
            <div style={s.sub}>{c.remainsAfter ? t.dlcRemainsAfter : t.dlcClearsAfter}</div>
            {c.categories.map((g) => (
              <div key={g.key} style={{ marginTop: "6rem" }}>
                <div style={s.row}>
                  <div>{t.dlcCategoryLine(t.dlcKinds[g.kind] ?? g.kind, g.count)}</div>
                  {g.jumps > 0 && (
                    <button style={s.smallButton} onClick={() => jump(g)}>
                      {t.goSee}
                    </button>
                  )}
                </div>
                <div style={{ color: removable(g.action) ? colors.bar : colors.sub }}>{t.dlcActions[g.action] ?? g.action}</div>
                <div style={s.sub}>{g.samples.map((x) => `${x.name} ×${fmt(x.count)}`).join(", ")}</div>
              </div>
            ))}
            {canRemove && (
              <div style={{ marginTop: "8rem" }}>
                {isConfirming && <div style={{ color: colors.warn }}>{t.dlcConfirm(deleting, rebranding, companies, c.blocked)}</div>}
                <div style={{ ...s.row, justifyContent: "flex-start", marginTop: "6rem" }}>
                  <button
                    style={{ ...s.smallButton, background: isConfirming ? "rgba(255, 112, 67, 0.6)" : s.smallButton.background }}
                    onClick={() => remove(c)}
                  >
                    {isConfirming ? t.dlcRemoveNow : t.dlcRemove}
                  </button>
                  {isConfirming && (
                    <button style={{ ...s.smallButton, marginLeft: "8rem" }} onClick={() => setConfirming(null)}>
                      {t.cancel}
                    </button>
                  )}
                </div>
              </div>
            )}
          </div>
        );
      })}
    </div>
  );
}
