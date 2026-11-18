namespace CityCheckup.Core.Tests;

[TestClass]
public sealed class UsedModsViewTests
{
    [TestMethod]
    public void ToJson_ListsSortedNamesWithCount()
    {
        var json = UsedModsView.ToJson(new[] { "CityCheckup", "Alpha" }, cleared: false, clearedCount: 0);
        Assert.AreEqual("{\"count\":2,\"names\":[\"Alpha\",\"CityCheckup\"],\"more\":0,\"cleared\":false,\"clearedCount\":0}", json);
    }

    [TestMethod]
    public void ToJson_TruncatesLongListAndReportsRest()
    {
        var names = Enumerable.Range(0, 12).Select(i => $"mod{i:00}").ToList();
        var json = UsedModsView.ToJson(names, cleared: false, clearedCount: 0);
        StringAssert.StartsWith(json, "{\"count\":12,\"names\":[\"mod00\",");
        StringAssert.Contains(json, "\"mod07\"],\"more\":4,");
        Assert.IsFalse(json.Contains("mod08"));
    }

    [TestMethod]
    public void ToJson_IgnoresBlankAndDuplicateNames()
    {
        var json = UsedModsView.ToJson(new string?[] { "A", "", null, "  ", "A" }, cleared: true, clearedCount: 3);
        Assert.AreEqual("{\"count\":1,\"names\":[\"A\"],\"more\":0,\"cleared\":true,\"clearedCount\":3}", json);
    }
}
