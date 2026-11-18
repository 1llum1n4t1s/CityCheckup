namespace CityCheckup.Core.Tests;

/// <summary>複数のテストクラスが共有する見本データ。</summary>
internal static class Fixtures
{
    /// <summary>
    /// 所見が 6 件出る重い街：速度の低下、経路探し待ち、1 フレームの超過、経路探しの内訳、職の不足、空きの届かなさ。
    /// 選んだ速度どおり（ActualSpeed = 1）でも、区間の平均で判定して所見が出ることを確かめる。
    /// </summary>
    public static LoadReport HeavyCity(UiLanguage language)
    {
        var jobs = new JobMarket();
        for (var i = 0; i < 2000; i++)
        {
            jobs.AddUnemployed(0, 0, 0, 0);
        }
        jobs.AddVacancies(0, 100, 9000, 0, 0);
        var pathfind = new PathfindWindow(new Dictionary<PathfindSource, PathfindCounter>
        {
            [PathfindSource.JobSearch] = new(30, 6, 6.0),
            [PathfindSource.Shopping] = new(70, 70, 2.0),
        }, 10, 10);
        var speed = new SpeedSampler();
        speed.Add(10, 1f, 0.5f, 0.6, 0.030, 100);
        return new LoadReport
        {
            SelectedSpeed = 1,
            ActualSpeed = 1f,
            Jobs = jobs,
            Pathfind = pathfind,
            Speed = speed.Close(10),
            Language = language,
        };
    }
}
