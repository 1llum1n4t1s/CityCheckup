namespace CityCheckup.Core.Tests;

[TestClass]
public sealed class LanguageTests
{
    [TestMethod]
    [DataRow("ja-JP", UiLanguage.Japanese)]
    [DataRow("JA", UiLanguage.Japanese)]
    [DataRow("de-DE", UiLanguage.English)]
    [DataRow(null, UiLanguage.English)]
    public void FromLocale_UsesJapaneseOnlyForJapanese(string? locale, UiLanguage expected)
        => Assert.AreEqual(expected, UiLanguages.FromLocale(locale));

    [TestMethod]
    public void Findings_AreWrittenInEnglishWhenAsked()
    {
        var texts = Fixtures.HeavyCity(UiLanguage.English).Findings().Select(f => f.Text).ToList();
        Assert.AreEqual(6, texts.Count, string.Join("\n", texts));
        Assert.IsTrue(texts[3].Contains("75% of pathfinding work is \"Job search\""), texts[3]);
        Assert.IsTrue(texts[3].Contains("Only 20% find a destination"), texts[3]);
        // 英語の文に日本語が混ざっていない（6 件すべて）。
        foreach (var text in texts)
        {
            Assert.IsFalse(text.Any(c => c >= 0x3040 && c <= 0x9FFF), text);
        }

        // 速度が落ちていない街でも、経路探しの偏りの所見は出る（遅れの判定の内側に入れない）。
        var steady = new LoadReport
        {
            SelectedSpeed = 1,
            ActualSpeed = 1,
            Pathfind = Fixtures.HeavyCity(UiLanguage.English).Pathfind,
            Language = UiLanguage.English,
        }.Findings().Single();
        Assert.IsTrue(steady.Text.Contains("75% of pathfinding work is \"Job search\""), steady.Text);
    }

    [TestMethod]
    public void HealthyCity_SaysSoInBothLanguages()
    {
        // 設定どおりの速度で動いている街では、警告ではなく「原因なし」の情報だけを出す。
        var english = new LoadReport { SelectedSpeed = 1, ActualSpeed = 1, Language = UiLanguage.English }.Findings().Single();
        var japanese = new LoadReport { SelectedSpeed = 1, ActualSpeed = 1 }.Findings().Single();
        Assert.AreEqual(FindingLevel.Info, english.Level);
        Assert.AreEqual(FindingLevel.Info, japanese.Level);
        Assert.AreEqual("No major cause of load was found.", english.Text);
        Assert.AreEqual("目立った負荷の原因は見つかっていません。", japanese.Text);
    }
}
