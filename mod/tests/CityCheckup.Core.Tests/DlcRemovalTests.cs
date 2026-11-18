namespace CityCheckup.Core.Tests;

[TestClass]
public sealed class DlcRemovalTests
{
    private static readonly EntityId Ports = new(2, 1);
    private static readonly EntityId Sf = new(1, 1);
    private static readonly EntityId Bus = new(10, 1);
    private static readonly EntityId Billboard = new(11, 1);
    private static readonly EntityId PortsBrand = new(12, 1);
    private static readonly EntityId SfCar = new(13, 1);
    private static readonly EntityId Pier = new(14, 1);

    private static DlcDiagnostics Sample() => new(
        new[]
        {
            new DlcContent(Ports, "BridgesAndPorts", 5, 1, missing: true),
            new DlcContent(Sf, "SanFranciscoSet", 1, 1, missing: false),
        },
        new[]
        {
            new DlcPrerequisite(Bus, "BusCO01", Ports, "BridgesAndPorts", DlcReach.ContentPrerequisite, holderEnabled: false),
            new DlcPrerequisite(Billboard, "Billboard \"Calamari\"", Ports, "BridgesAndPorts", DlcReach.ContentPrerequisite, holderEnabled: false),
            new DlcPrerequisite(PortsBrand, "CalamariTangle", Ports, "BridgesAndPorts", DlcReach.ContentPrerequisite, holderEnabled: false),
            new DlcPrerequisite(Pier, "Pier", Ports, "BridgesAndPorts", DlcReach.ContentPrerequisite, holderEnabled: false),
            new DlcPrerequisite(SfCar, "MuscleCar01", Sf, "SanFranciscoSet", DlcReach.ContentPrerequisite, holderEnabled: true),
        });

    private static void Add(DlcDiagnostics d, DlcRefPath path, EntityId target, DlcClass cls, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (d.Record(path, target, cls))
            {
                var s = new DlcReferenceSample(new EntityId(1000 + i, 1), "holder", true, i, 0, 0);
                d.AddSample(path, target, cls, s, s);
            }
        }
    }

    [TestMethod]
    public void Classify_LoadedTargetNeedsDlcDisabled()
    {
        Assert.AreEqual(DlcAction.NeedsDlcDisabled, DlcRemoval.Classify(DlcRefPath.PrefabRef, DlcHolderKind.Vehicle, targetMissing: false, canAct: true).Action);
        Assert.AreEqual(DlcAction.NeedsDlcDisabled, DlcRemoval.Classify(DlcRefPath.CompanyBrand, DlcHolderKind.Company, false, true).Action);
        Assert.AreEqual(DlcAction.NeedsDlcDisabled, DlcRemoval.Classify(DlcRefPath.VehicleModelPrimary, DlcHolderKind.Other, false, false).Action);
    }

    [TestMethod]
    public void Classify_MissingTargetsByKind()
    {
        Assert.AreEqual(new DlcClass(DlcCategory.Vehicle, DlcAction.Delete, DlcHolderKind.Vehicle, true),
            DlcRemoval.Classify(DlcRefPath.PrefabRef, DlcHolderKind.Vehicle, true, false));
        Assert.AreEqual(DlcAction.RebuildDelete, DlcRemoval.Classify(DlcRefPath.PrefabRef, DlcHolderKind.SubObject, true, true).Action);
        Assert.AreEqual(DlcAction.Delete, DlcRemoval.Classify(DlcRefPath.PrefabRef, DlcHolderKind.Building, true, true).Action);
        Assert.AreEqual(DlcAction.ListOnly, DlcRemoval.Classify(DlcRefPath.PrefabRef, DlcHolderKind.Building, true, false).Action);
        Assert.AreEqual(DlcCategory.Building, DlcRemoval.Classify(DlcRefPath.PrefabRef, DlcHolderKind.PlacedObject, true, true).Category);
        Assert.AreEqual(DlcAction.Rebrand, DlcRemoval.Classify(DlcRefPath.CompanyBrand, DlcHolderKind.Company, true, true).Action);
        Assert.AreEqual(DlcAction.NoCandidate, DlcRemoval.Classify(DlcRefPath.CompanyBrand, DlcHolderKind.Company, true, false).Action);
        Assert.AreEqual(DlcAction.ClearModel, DlcRemoval.Classify(DlcRefPath.VehicleModelSecondary, DlcHolderKind.Other, true, false).Action);
    }

    [TestMethod]
    public void Classify_NetAreaAndOtherPathsAreListOnly()
    {
        foreach (var missing in new[] { true, false })
        {
            Assert.AreEqual(new DlcClass(DlcCategory.NetArea, DlcAction.ListOnly, DlcHolderKind.Net, missing),
                DlcRemoval.Classify(DlcRefPath.PrefabRef, DlcHolderKind.Net, missing, true));
            Assert.AreEqual(DlcCategory.NetArea, DlcRemoval.Classify(DlcRefPath.PrefabRef, DlcHolderKind.Area, missing, true).Category);
            Assert.AreEqual(DlcAction.ListOnly, DlcRemoval.Classify(DlcRefPath.PrefabRef, DlcHolderKind.Creature, missing, true).Action);
            foreach (var path in new[]
            {
                DlcRefPath.Policy, DlcRefPath.ServiceBudget, DlcRefPath.ChirpSender, DlcRefPath.ChirpEntity,
                DlcRefPath.SubReplacement, DlcRefPath.UnderConstruction, DlcRefPath.EditorContainer,
            })
            {
                var c = DlcRemoval.Classify(path, DlcHolderKind.Other, missing, true);
                Assert.AreEqual(DlcCategory.Other, c.Category, path.ToString());
                Assert.AreEqual(DlcAction.ListOnly, c.Action, path.ToString());
            }
        }
    }

    [TestMethod]
    public void Classify_CompanyWithMissingPrefabMovesAway()
    {
        // 企業 prefab が欠落した企業は転出させる（取り除ける）。DLC が有効なら読み込み直しの案内。
        Assert.AreEqual(new DlcClass(DlcCategory.Company, DlcAction.MoveAway, DlcHolderKind.Company, true),
            DlcRemoval.Classify(DlcRefPath.PrefabRef, DlcHolderKind.Company, true, false));
        Assert.AreEqual(DlcAction.NeedsDlcDisabled, DlcRemoval.Classify(DlcRefPath.PrefabRef, DlcHolderKind.Company, false, false).Action);
        Assert.IsTrue(DlcRemoval.IsRemovable(DlcAction.MoveAway));
        Assert.AreEqual("moveAway", DlcRemoval.ActionKey(DlcAction.MoveAway));
    }

    [TestMethod]
    public void Classify_BrandOfMovingCompanyIsRemovedWithIt()
    {
        // 付け替え先が無くても、企業ごと転出するならブランドの参照も消える（残りに出さない）。
        var c = DlcRemoval.Classify(DlcRefPath.CompanyBrand, DlcHolderKind.Company, targetMissing: true, canAct: false, holderMovesAway: true);
        Assert.AreEqual(new DlcClass(DlcCategory.Brand, DlcAction.MoveAway, DlcHolderKind.Company, true), c);
        Assert.AreEqual(DlcAction.NoCandidate, DlcRemoval.Classify(DlcRefPath.CompanyBrand, DlcHolderKind.Company, true, false).Action);
        // DLC が有効なブランドは、企業が転出しても読み込み直しの案内のまま（取り除くのは欠落した参照だけ）。
        Assert.AreEqual(DlcAction.NeedsDlcDisabled,
            DlcRemoval.Classify(DlcRefPath.CompanyBrand, DlcHolderKind.Company, false, false, holderMovesAway: true).Action);
    }

    [TestMethod]
    public void BuildView_MovingCompaniesClearContentAndAreNotDoubleCounted()
    {
        var fishCompany = new EntityId(15, 1);
        var d = new DlcDiagnostics(
            new[] { new DlcContent(Ports, "BridgesAndPorts", 5, 1, missing: true) },
            new[]
            {
                new DlcPrerequisite(fishCompany, "Industrial_FoodFromFish", Ports, "BridgesAndPorts", DlcReach.ContentPrerequisite, holderEnabled: false),
                new DlcPrerequisite(PortsBrand, "CalamariTangle", Ports, "BridgesAndPorts", DlcReach.ContentPrerequisite, holderEnabled: false),
            });
        Add(d, DlcRefPath.PrefabRef, fishCompany, DlcRemoval.Classify(DlcRefPath.PrefabRef, DlcHolderKind.Company, true, false), 35);
        // 走査は企業 prefab がブランドを覆うときブランドを数えないが、覆わない場合に備えて両方ある形でも二重に数えないことを確かめる。
        Add(d, DlcRefPath.CompanyBrand, PortsBrand, DlcRemoval.Classify(DlcRefPath.CompanyBrand, DlcHolderKind.Company, true, false, true), 35);

        var ports = d.BuildView().Single();
        Assert.AreEqual(0, ports.Blocked);
        Assert.IsFalse(ports.RemainsAfter);
        Assert.AreEqual(35, ports.CompaniesMovingAway);
        Assert.AreEqual(0, d.PredictedAfterRemoval().Count);
        CollectionAssert.AreEquivalent(new[] { fishCompany, PortsBrand }, d.RemovalTargets("BridgesAndPorts").ToList());
        StringAssert.Contains(DlcView.ToJson("ready", d.BuildView(), Array.Empty<string>(), Array.Empty<string>(), null), "\"companies\":35");
    }

    [TestMethod]
    public void SelectBrand_NoCandidateReturnsMinusOneInsteadOfNull()
    {
        var none = new HashSet<EntityId>();
        Assert.AreEqual(-1, DlcRemoval.SelectBrand(Array.Empty<DlcBrandCandidate>(), none, 7));
        var onlyMissing = new[] { new DlcBrandCandidate(new EntityId(5, 1), missing: true, Array.Empty<EntityId>()) };
        Assert.AreEqual(-1, DlcRemoval.SelectBrand(onlyMissing, none, 7));
        // Entity.Null（0:0）が候補に紛れていても選ばない。
        var nullBrand = new[] { new DlcBrandCandidate(default, missing: false, Array.Empty<EntityId>()) };
        Assert.AreEqual(-1, DlcRemoval.SelectBrand(nullBrand, none, 7));
    }

    [TestMethod]
    public void SelectBrand_ExcludesMissingAndForbiddenContentBrands()
    {
        var forbidden = new HashSet<EntityId> { Ports };
        var candidates = new[]
        {
            new DlcBrandCandidate(new EntityId(5, 1), missing: true, Array.Empty<EntityId>()),
            new DlcBrandCandidate(new EntityId(6, 1), missing: false, new[] { Ports }),
            new DlcBrandCandidate(new EntityId(7, 1), missing: false, new[] { Sf }),
        };
        // 前提なしのブランドが無いので、取り除く対象以外の前提を持つブランドへ。
        for (uint seed = 0; seed < 10; seed++)
        {
            Assert.AreEqual(2, DlcRemoval.SelectBrand(candidates, forbidden, seed));
        }
    }

    [TestMethod]
    public void SelectBrand_PrefersBrandsWithoutAnyContentPrerequisite()
    {
        var forbidden = new HashSet<EntityId> { Ports };
        var candidates = new[]
        {
            new DlcBrandCandidate(new EntityId(7, 1), missing: false, new[] { Sf }),
            new DlcBrandCandidate(new EntityId(8, 1), missing: false, Array.Empty<EntityId>()),
            new DlcBrandCandidate(new EntityId(9, 1), missing: false, Array.Empty<EntityId>()),
        };
        var chosen = new HashSet<int>();
        for (uint seed = 0; seed < 10; seed++)
        {
            var i = DlcRemoval.SelectBrand(candidates, forbidden, seed);
            // 使える候補（1 と 2）の中から、種を候補数で割った余りで選ぶ（同じ街なら同じ結果）。
            Assert.AreEqual(1 + (int)(seed % 2), i);
            // 同じ種なら同じブランドになる。
            Assert.AreEqual(i, DlcRemoval.SelectBrand(candidates, forbidden, seed));
            chosen.Add(i);
        }
        CollectionAssert.AreEquivalent(new[] { 1, 2 }, chosen.ToList());
    }

    [TestMethod]
    public void RewriteVehicleModels_ClearsOnlyMissingSidesAndDropsEmptyElements()
    {
        var keep = new EntityId(30, 1);
        var models = new[]
        {
            (Bus, keep),          // 片側だけ欠落 → 残る
            (Bus, Bus),           // 両側欠落 → 要素ごと消える
            (keep, default(EntityId)),
            (default(EntityId), Bus), // 欠落を外すと両方 Null → 消える
        };
        var result = DlcRemoval.RewriteVehicleModels(models, id => id.Equals(Bus), out var cleared);
        CollectionAssert.AreEqual(new[] { (default(EntityId), keep), (keep, default(EntityId)) }, result);
        Assert.AreEqual(4, cleared);
    }

    [TestMethod]
    public void RewriteVehicleModels_AllMissingBecomesEmpty()
    {
        var result = DlcRemoval.RewriteVehicleModels(new[] { (Bus, default(EntityId)) }, id => id.Equals(Bus), out var cleared);
        Assert.AreEqual(0, result.Count);
        Assert.AreEqual(1, cleared);
    }

    [TestMethod]
    public void PredictedAfterRemoval_KeepsContentWithListOnlyReferences()
    {
        var d = Sample();
        Add(d, DlcRefPath.PrefabRef, Bus, DlcRemoval.Classify(DlcRefPath.PrefabRef, DlcHolderKind.Vehicle, true, false), 2);
        Add(d, DlcRefPath.PrefabRef, SfCar, DlcRemoval.Classify(DlcRefPath.PrefabRef, DlcHolderKind.Vehicle, false, false), 1);
        CollectionAssert.AreEqual(new[] { "BridgesAndPorts", "SanFranciscoSet" }, d.PredictedContents().Select(c => c.Name).ToList());
        // 車両は取り除けるので Ports は消える。SF は DLC が有効なままなので残る。
        CollectionAssert.AreEqual(new[] { "SanFranciscoSet" }, d.PredictedAfterRemoval().Select(c => c.Name).ToList());

        Add(d, DlcRefPath.PrefabRef, Pier, DlcRemoval.Classify(DlcRefPath.PrefabRef, DlcHolderKind.Net, true, false), 1);
        CollectionAssert.AreEqual(new[] { "BridgesAndPorts", "SanFranciscoSet" }, d.PredictedAfterRemoval().Select(c => c.Name).ToList());

        var text = string.Join("\n", d.BuildLines(null, "x"));
        StringAssert.Contains(text, "dlc   categories: netArea/listOnly=1 vehicle/delete=2");
        StringAssert.Contains(text, "dlc holder kinds path=PrefabRef: vehicle=3(missing 2) net=1(missing 1)");
        StringAssert.Contains(text, "dlc predicted after removal (would still require): BridgesAndPorts, SanFranciscoSet");
    }

    [TestMethod]
    public void RemovalTargets_OnlyMissingTargetsOfThatContent()
    {
        var d = Sample();
        Add(d, DlcRefPath.PrefabRef, Bus, DlcRemoval.Classify(DlcRefPath.PrefabRef, DlcHolderKind.Vehicle, true, false), 1);
        Add(d, DlcRefPath.CompanyBrand, PortsBrand, DlcRemoval.Classify(DlcRefPath.CompanyBrand, DlcHolderKind.Company, true, true), 1);
        Add(d, DlcRefPath.PrefabRef, SfCar, DlcRemoval.Classify(DlcRefPath.PrefabRef, DlcHolderKind.Vehicle, false, false), 1);
        CollectionAssert.AreEquivalent(new[] { Bus, PortsBrand }, d.RemovalTargets("BridgesAndPorts").ToList());
        Assert.AreEqual(0, d.RemovalTargets("SanFranciscoSet").Count);
        Assert.AreEqual(0, d.RemovalTargets("Unknown").Count);
        CollectionAssert.AreEquivalent(new[] { Sf }, d.RequiredContentsOf(SfCar).ToList());
        CollectionAssert.AreEqual(new[] { Ports }, d.MissingContents().ToList());
    }

    [TestMethod]
    public void BuildView_GroupsByCategoryAndAction()
    {
        var d = Sample();
        Add(d, DlcRefPath.PrefabRef, Bus, DlcRemoval.Classify(DlcRefPath.PrefabRef, DlcHolderKind.Vehicle, true, false), 5);
        Add(d, DlcRefPath.PrefabRef, Billboard, DlcRemoval.Classify(DlcRefPath.PrefabRef, DlcHolderKind.SubObject, true, true), 3);
        Add(d, DlcRefPath.CompanyBrand, PortsBrand, DlcRemoval.Classify(DlcRefPath.CompanyBrand, DlcHolderKind.Company, true, false), 2);
        Add(d, DlcRefPath.PrefabRef, SfCar, DlcRemoval.Classify(DlcRefPath.PrefabRef, DlcHolderKind.Vehicle, false, false), 250);

        var view = d.BuildView();
        Assert.AreEqual(2, view.Count);
        var ports = view.Single(v => v.Key == "BridgesAndPorts");
        Assert.AreEqual(DlcContentStatus.Missing, ports.Status);
        Assert.AreEqual(8, ports.Removable);
        Assert.AreEqual(2, ports.Blocked);
        Assert.IsTrue(ports.RemainsAfter);
        CollectionAssert.AreEqual(
            new[] { "BridgesAndPorts|vehicle|delete", "BridgesAndPorts|brand|noCandidate", "BridgesAndPorts|subObject|rebuildDelete" },
            ports.Categories.Select(c => c.Key).ToList());

        var sf = view.Single(v => v.Key == "SanFranciscoSet");
        Assert.AreEqual(DlcContentStatus.Loaded, sf.Status);
        Assert.AreEqual(DlcAction.NeedsDlcDisabled, sf.Categories.Single().Action);
        Assert.AreEqual(DlcDiagnostics.JumpsPerCategory, sf.Categories.Single().Jumps.Count);
        StringAssert.Contains(DlcView.ToJson("ready", view, Array.Empty<string>(), Array.Empty<string>(), null), "\"status\":\"loaded\"");
    }

    [TestMethod]
    public void BuildView_MixedWhenSomeTargetsMissing()
    {
        var d = Sample();
        Add(d, DlcRefPath.PrefabRef, Bus, DlcRemoval.Classify(DlcRefPath.PrefabRef, DlcHolderKind.Vehicle, true, false), 1);
        Add(d, DlcRefPath.PrefabRef, Pier, DlcRemoval.Classify(DlcRefPath.PrefabRef, DlcHolderKind.Building, false, true), 1);
        Assert.AreEqual(DlcContentStatus.Mixed, d.BuildView().Single().Status);
    }

    [TestMethod]
    public void ToJson_ShapeAndEscaping()
    {
        var d = Sample();
        Add(d, DlcRefPath.PrefabRef, Billboard, DlcRemoval.Classify(DlcRefPath.PrefabRef, DlcHolderKind.SubObject, true, true), 2);
        var last = new DlcRemovalResult("BridgesAndPorts") { Deleted = 3, Rebranded = 1, MovedAway = 5, RoutesCleared = 2, Skipped = 4, OrphansCleaned = 6, Failed = true };
        var json = DlcView.ToJson("ready", d.BuildView(), d.PredictedContents().Select(c => c.Name), d.PredictedAfterRemoval().Select(c => c.Name), last);
        StringAssert.StartsWith(json, "{\"state\":\"ready\",\"last\":{\"content\":\"BridgesAndPorts\",\"deleted\":3,\"rebranded\":1,\"movedAway\":5,\"routesCleared\":2,\"skipped\":4,\"orphansCleaned\":6,\"failed\":true}");
        StringAssert.Contains(json, "\"predicted\":[\"BridgesAndPorts\"],\"predictedAfter\":[]");
        StringAssert.Contains(json, "{\"key\":\"BridgesAndPorts\",\"name\":\"BridgesAndPorts\",\"dlcId\":5,\"status\":\"missing\",\"required\":true,\"remainsAfter\":false,\"removable\":2,\"blocked\":0,\"companies\":0");
        StringAssert.Contains(json, "{\"key\":\"BridgesAndPorts|subObject|rebuildDelete\",\"kind\":\"subObject\",\"action\":\"rebuildDelete\",\"count\":2,\"jumps\":2,\"samples\":[{\"name\":\"Billboard \\\"Calamari\\\"\",\"count\":2}]}");

        // 失敗していない取り除きは "failed":false（成功のたびに失敗表示が出ないこと）。
        var ok = DlcView.ToJson("ready", d.BuildView(), Array.Empty<string>(), Array.Empty<string>(), new DlcRemovalResult("BridgesAndPorts"));
        StringAssert.Contains(ok, "\"failed\":false}");

        var empty = DlcView.ToJson("idle", Array.Empty<DlcContentView>(), Array.Empty<string>(), Array.Empty<string>(), null);
        Assert.AreEqual("{\"state\":\"idle\",\"last\":null,\"predicted\":[],\"predictedAfter\":[],\"contents\":[]}", empty);
    }

    [TestMethod]
    public void BuildLines_ClassifiedLoadedListOnlyReferencesAreNotDropped()
    {
        var d = Sample();
        // 有効な参照先への Policy と、PrefabRef の「その他」は (Other, ListOnly, Other, 有効) に分類される。分類なしと混同せず数える。
        Add(d, DlcRefPath.Policy, SfCar, DlcRemoval.Classify(DlcRefPath.Policy, DlcHolderKind.Other, false, false), 2);
        Add(d, DlcRefPath.PrefabRef, SfCar, DlcRemoval.Classify(DlcRefPath.PrefabRef, DlcHolderKind.Other, false, false), 3);
        var text = string.Join("\n", d.BuildLines(null, "x"));
        StringAssert.Contains(text, "dlc   categories: other/listOnly=5");
        StringAssert.Contains(text, "dlc holder kinds path=PrefabRef: other=3(missing 0)");
        StringAssert.Contains(text, "dlc holder kinds path=Policy: other=2(missing 0)");
        CollectionAssert.AreEqual(new[] { "SanFranciscoSet" }, d.PredictedAfterRemoval().Select(c => c.Name).ToList());
        Assert.AreEqual(5, d.BuildView().Single().Blocked);
    }

    [TestMethod]
    public void UnclassifiedRecords_CountOnlyForPathLines()
    {
        var d = Sample();
        d.Record(DlcRefPath.PrefabRef, Bus);
        Assert.AreEqual(0, d.RemovalTargets("BridgesAndPorts").Count);
        Assert.AreEqual(0, d.PredictedAfterRemoval().Count);
        var text = string.Join("\n", d.BuildLines(null, "x"));
        StringAssert.Contains(text, "path=PrefabRef refs=1");
        Assert.IsFalse(text.Contains("dlc holder kinds"));
    }

    [TestMethod]
    public void Covers_OnlyWhenOwnerReachesEveryContentOfTarget()
    {
        var d = Sample();
        Assert.IsTrue(d.Covers(Pier, Billboard));
        Assert.IsFalse(d.Covers(SfCar, Billboard));
        Assert.IsFalse(d.Covers(new EntityId(99, 1), Billboard));

        // Two は Ports と Sf の両方に届く。片方にしか届かない Pier は Two を覆えないが、Two は Pier を覆う。
        var two = new EntityId(40, 1);
        var both = new DlcDiagnostics(
            new[]
            {
                new DlcContent(Ports, "BridgesAndPorts", 5, 1, missing: true),
                new DlcContent(Sf, "SanFranciscoSet", 1, 1, missing: false),
            },
            new[]
            {
                new DlcPrerequisite(Pier, "Pier", Ports, "BridgesAndPorts", DlcReach.ContentPrerequisite, holderEnabled: false),
                new DlcPrerequisite(two, "Two", Ports, "BridgesAndPorts", DlcReach.ContentPrerequisite, holderEnabled: false),
                new DlcPrerequisite(two, "Two", Sf, "SanFranciscoSet", DlcReach.ContentPrerequisite, holderEnabled: false),
            });
        Assert.IsFalse(both.Covers(Pier, two));
        Assert.IsTrue(both.Covers(two, Pier));
    }

    [TestMethod]
    public void Classify_SubObjectWhoseTopOwnerIsMissingIsListOnly()
    {
        // 最上位の持ち主の prefab が欠落していて消されない付属物は、組み直させずに一覧だけ（DLC の有無に関係なく）。
        Assert.AreEqual(new DlcClass(DlcCategory.SubObject, DlcAction.ListOnly, DlcHolderKind.SubObject, true),
            DlcRemoval.Classify(DlcRefPath.PrefabRef, DlcHolderKind.SubObject, targetMissing: true, canAct: false));
        Assert.AreEqual(DlcAction.ListOnly, DlcRemoval.Classify(DlcRefPath.PrefabRef, DlcHolderKind.SubObject, false, false).Action);
        // 組み直させてよい付属物でも、DLC が有効なら読み込み直しの案内。
        Assert.AreEqual(DlcAction.NeedsDlcDisabled, DlcRemoval.Classify(DlcRefPath.PrefabRef, DlcHolderKind.SubObject, false, true).Action);
    }

    [TestMethod]
    public void PredictedAfterRemoval_SubObjectWithMissingTopOwnerKeepsContent()
    {
        var d = Sample();
        Add(d, DlcRefPath.PrefabRef, Billboard, DlcRemoval.Classify(DlcRefPath.PrefabRef, DlcHolderKind.SubObject, true, true), 2);
        Assert.AreEqual(0, d.PredictedAfterRemoval().Count);
        Add(d, DlcRefPath.PrefabRef, Billboard, DlcRemoval.Classify(DlcRefPath.PrefabRef, DlcHolderKind.SubObject, true, false), 1);
        var ports = d.BuildView().Single();
        Assert.AreEqual(2, ports.Removable);
        Assert.AreEqual(1, ports.Blocked);
        Assert.IsTrue(ports.RemainsAfter);
        CollectionAssert.AreEqual(new[] { "BridgesAndPorts" }, d.PredictedAfterRemoval().Select(c => c.Name).ToList());
        CollectionAssert.AreEqual(
            new[] { "BridgesAndPorts|subObject|rebuildDelete", "BridgesAndPorts|subObject|listOnly" },
            ports.Categories.Select(c => c.Key).ToList());
    }

    [TestMethod]
    public void Classify_SystemReferencesAreListOnly()
    {
        var paths = new[]
        {
            DlcRefPath.CityTheme, DlcRefPath.CityRequiredContent, DlcRefPath.Climate,
            DlcRefPath.TerrainRenderSettings, DlcRefPath.WaterRenderSettings, DlcRefPath.TransportUsage,
        };
        foreach (var missing in new[] { true, false })
        {
            foreach (var path in paths)
            {
                var c = DlcRemoval.Classify(path, DlcHolderKind.Other, missing, true);
                Assert.AreEqual(new DlcClass(DlcCategory.System, DlcAction.ListOnly, DlcHolderKind.Other, missing), c, path.ToString());
                Assert.IsFalse(DlcRemoval.IsRemovable(c.Action));
            }
        }
        Assert.AreEqual("system", DlcRemoval.CategoryKey(DlcCategory.System));
    }

    [TestMethod]
    public void SystemReferences_CountInPredictionBeforeAndAfterRemoval()
    {
        var d = Sample();
        // マップが要求するコンテンツ（ContentPrefab そのもの）と、輸送の記録のキー（前提を持つ建物 prefab）。
        Add(d, DlcRefPath.CityRequiredContent, Sf, DlcRemoval.Classify(DlcRefPath.CityRequiredContent, DlcHolderKind.Other, false, false), 1);
        Add(d, DlcRefPath.TransportUsage, Pier, DlcRemoval.Classify(DlcRefPath.TransportUsage, DlcHolderKind.Other, true, false), 1);
        // 取り除ける参照があっても、システムが持つ参照が残るコンテンツは取り除いた後も要求される。
        Add(d, DlcRefPath.PrefabRef, Bus, DlcRemoval.Classify(DlcRefPath.PrefabRef, DlcHolderKind.Vehicle, true, false), 4);
        CollectionAssert.AreEqual(new[] { "BridgesAndPorts", "SanFranciscoSet" }, d.PredictedContents().Select(c => c.Name).ToList());
        CollectionAssert.AreEqual(new[] { "BridgesAndPorts", "SanFranciscoSet" }, d.PredictedAfterRemoval().Select(c => c.Name).ToList());

        var ports = d.BuildView().Single(v => v.Key == "BridgesAndPorts");
        Assert.AreEqual(4, ports.Removable);
        Assert.AreEqual(1, ports.Blocked);
        var system = ports.Categories.Single(c => c.Category == DlcCategory.System);
        Assert.AreEqual(DlcAction.ListOnly, system.Action);
        // 輸送の記録は消さない（一覧だけ）。取り除きは PrefabRef などの照会からだけ行い、システムが持つ参照には触らない。
        Assert.AreEqual("BridgesAndPorts|system|listOnly", system.Key);
        var text = string.Join("\n", d.BuildLines(null, "x"));
        StringAssert.Contains(text, "path=TransportUsage refs=1");
        StringAssert.Contains(text, "path=CityRequiredContent refs=1");
    }
}
