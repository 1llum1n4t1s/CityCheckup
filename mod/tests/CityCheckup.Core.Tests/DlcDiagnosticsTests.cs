namespace CityCheckup.Core.Tests;

[TestClass]
public sealed class DlcDiagnosticsTests
{
    private static readonly EntityId Sf = new(1, 1);
    private static readonly EntityId Ports = new(2, 1);
    private static readonly EntityId Plain = new(3, 1);
    private static readonly EntityId SfHouse = new(10, 1);
    private static readonly EntityId PortsPier = new(11, 1);
    private static readonly EntityId ModPart = new(12, 1);
    private static readonly EntityId Free = new(13, 1);

    private static DlcDiagnostics Sample() => new(
        new[]
        {
            new DlcContent(Sf, "SanFranciscoSet", 1, 1, missing: false),
            new DlcContent(Ports, "BridgesAndPorts", 5, 1, missing: true),
            new DlcContent(Plain, "Unused", 99, 5, missing: false),
        },
        new[]
        {
            new DlcPrerequisite(SfHouse, "SF House", Sf, "SanFranciscoSet", DlcReach.ContentPrerequisite, holderEnabled: true),
            new DlcPrerequisite(PortsPier, "Pier", Ports, "BridgesAndPorts", DlcReach.ContentPrerequisite, holderEnabled: false),
            new DlcPrerequisite(ModPart, "Mod Part", Sf, "SanFranciscoSet", DlcReach.ModPrerequisite, holderEnabled: true),
        });

    [TestMethod]
    public void IsRelevant_OnlyForContentAndPrerequisiteHolders()
    {
        var d = Sample();
        Assert.IsTrue(d.IsRelevant(Sf));
        Assert.IsTrue(d.IsRelevant(SfHouse));
        Assert.IsFalse(d.IsRelevant(Free));
    }

    [TestMethod]
    public void Record_IgnoresIrrelevantTargetsAndLimitsSamples()
    {
        var d = Sample();
        Assert.IsFalse(d.Record(DlcRefPath.PrefabRef, Free));
        for (var i = 0; i < DlcDiagnostics.SamplesPerTarget; i++)
        {
            Assert.IsTrue(d.Record(DlcRefPath.PrefabRef, SfHouse));
            d.AddSample(DlcRefPath.PrefabRef, SfHouse, new DlcReferenceSample(new EntityId(100 + i, 1), "SF House", false, 0, 0, 0));
        }
        Assert.IsFalse(d.Record(DlcRefPath.PrefabRef, SfHouse));
        var text = string.Join("\n", d.BuildLines(null, "x"));
        Assert.AreEqual(DlcDiagnostics.SamplesPerGroup, text.Split('\n').Count(l => l.Contains("sample ")));
        StringAssert.Contains(text, "via=ContentPrerequisite path=PrefabRef refs=4 targetPrefabs=1");
    }

    [TestMethod]
    public void Predicted_IncludesPrerequisiteAndDirectReferences_ExcludesUnreferenced()
    {
        var d = Sample();
        d.Record(DlcRefPath.PrefabRef, SfHouse);
        d.Record(DlcRefPath.VehicleModelPrimary, Ports);
        var names = d.PredictedContents().Select(c => c.Name).ToList();
        CollectionAssert.AreEqual(new[] { "BridgesAndPorts", "SanFranciscoSet" }, names);
    }

    [TestMethod]
    public void Predicted_ModPrerequisiteReachesContent()
    {
        var d = Sample();
        d.Record(DlcRefPath.Policy, ModPart);
        CollectionAssert.AreEqual(new[] { "SanFranciscoSet" }, d.PredictedContents().Select(c => c.Name).ToList());
    }

    [TestMethod]
    public void Predicted_ExcludesContentWithoutContentData()
    {
        var holder = new EntityId(20, 1);
        var ghost = new EntityId(21, 1);
        var d = new DlcDiagnostics(
            Array.Empty<DlcContent>(),
            new[] { new DlcPrerequisite(holder, "Holder", ghost, "Ghost", DlcReach.ContentPrerequisite, holderEnabled: true) });
        d.Record(DlcRefPath.PrefabRef, holder);
        Assert.AreEqual(0, d.PredictedContents().Count);
        StringAssert.Contains(string.Join("\n", d.BuildLines(null, "x")), "(no ContentData)");
    }

    [TestMethod]
    public void BuildLines_CountsHoldersRegardlessOfEnabledStateAndShowsMetadata()
    {
        var d = Sample();
        d.Record(DlcRefPath.PrefabRef, Sf);
        var text = string.Join("\n", d.BuildLines(new[] { "SanFranciscoSet" }, null));
        StringAssert.Contains(text, "'BridgesAndPorts' entity=2:1 dlcId=5(BridgesAndPorts) flags=RequireDlc missing=yes prerequisiteHolders=1 (content=1 mod=0; prefabData enabled=0 disabled=1)");
        StringAssert.Contains(text, "'SanFranciscoSet' entity=1:1 dlcId=1(SanFranciscoSet) flags=RequireDlc missing=no prerequisiteHolders=2 (content=1 mod=1; prefabData enabled=2 disabled=0)");
        StringAssert.Contains(text, "via=Direct path=PrefabRef refs=1 targetPrefabs=1");
        StringAssert.Contains(text, "dlc predicted (a save now would require): SanFranciscoSet");
        StringAssert.Contains(text, "dlc save metadata contentPrerequisites: SanFranciscoSet");
    }

    [TestMethod]
    public void BuildLines_ReportsUnavailableMetadataWithReason()
    {
        var text = string.Join("\n", Sample().BuildLines(null, "no asset"));
        StringAssert.Contains(text, "contentPrerequisites: unavailable (no asset)");
        StringAssert.Contains(text, "dlc predicted (a save now would require): (none)");
    }

    [TestMethod]
    public void Sample_ShowsEntityPrefabsAndPosition()
    {
        var d = Sample();
        Assert.IsTrue(d.Record(DlcRefPath.UnderConstruction, SfHouse));
        d.AddSample(DlcRefPath.UnderConstruction, SfHouse, new DlcReferenceSample(new EntityId(500, 7), "Lot", true, 1.5f, 2f, -3f));
        StringAssert.Contains(string.Join("\n", d.BuildLines(null, null)), "sample entity=500:7 holderPrefab='Lot' target='SF House' pos=(1.5, 2.0, -3.0)");
    }

    [TestMethod]
    public void FormatFlags_AndDlcId()
    {
        Assert.AreEqual("None", DlcDiagnostics.FormatFlags(0));
        Assert.AreEqual("RequireDlc|RequireMod", DlcDiagnostics.FormatFlags(5));
        Assert.AreEqual("RequireDlc|0x10", DlcDiagnostics.FormatFlags(0x11));
        Assert.AreEqual("5(BridgesAndPorts)", DlcDiagnostics.FormatDlcId(5));
        Assert.AreEqual("77", DlcDiagnostics.FormatDlcId(77));
    }
}
