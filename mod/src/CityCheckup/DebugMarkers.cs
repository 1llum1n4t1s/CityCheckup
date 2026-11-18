using System.IO;

namespace CityCheckup;

/// <summary>
/// 動作確認用の目印ファイル。Mods\CityCheckup\ に置いたときだけ有効になる。
/// </summary>
/// <remarks>
/// 画面を見たり操作したりできない検証機（リモートのゲーム PC）で、UI 操作の代わりに処理を自動で動かし、
/// 結果を Player.log と画面写真に残すために使う。通常の利用者の環境には置かれない。
/// - debug-report : 負荷分析を開いた状態で始め、集計結果をログに書き、画面を保存する
/// - debug-missing: 欠落アセットを探して一覧をログに書き、最初の 1 か所へカメラを移して画面を保存する
/// - debug-delete : debug-missing に加えて、消してよいものを全部消し、消した後の状態をログに書く
/// - debug-dlc    : 読み込み完了の数秒後に 1 回、セーブが DLC を要求する原因（参照の経路ごとの件数と見本、保有 Entity の種類別の内訳、次の保存で要求される予測）をログに書く（読み取りのみ）
/// - debug-dlc-remove: パネルを DLC タブで開き、一覧の画面を保存してから、欠落したコンテンツを取り除く（ファイルの中身にコンテンツ名を 1 行ずつ書けばそれだけ、空なら欠落している全部）。
///                     直後と時間を進めた後（debug-speed3 があれば速度 ×3、止まっていれば ×1 で約 60 秒）に探し直し、宙に浮いた参照の数と一緒にログに書き、画面を保存する
/// - debug-speed3 : debug-report と一緒に使い、速度を ×3 に保つ（遅れの原因の切り分けを確かめるため）
/// - debug-lang-en: ゲームの言語設定に関係なく、パネルと所見を英語で表示する（英語表示を確かめるため）
/// </remarks>
internal static class DebugMarkers
{
    public static string Folder => Path.Combine(UnityEngine.Application.persistentDataPath, "Mods", "CityCheckup");

    public static bool Has(string name) => File.Exists(Path.Combine(Folder, name));

    public static string PathOf(string name) => Path.Combine(Folder, name);
}
