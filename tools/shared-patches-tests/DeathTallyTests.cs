using DualSouls.Mods;
using Xunit;

public sealed class DeathTallyTests : IDisposable
{
    readonly string _root = Path.Combine(Environment.GetEnvironmentVariable("DUALSOULS_HOST_TEST_TEMP") ??
        throw new InvalidOperationException("Set DUALSOULS_HOST_TEST_TEMP for disposable fixture payloads."),
        "death-tally-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    static readonly Func<bool> NoBoss = () => false;
    static readonly Func<bool> Boss = () => true;

    static void Die(DeathTally tally, object save, int profile, float time, string scene, Func<bool> boss)
    {
        tally.Tick(save, profile, time, scene, false, boss);
        tally.Tick(save, profile, time, scene, true, boss);
        tally.Tick(save, profile, time, scene, true, boss);   // still dead: one death
    }

    [Fact]
    public void EachDeathCountsOnceAndSurvivesReloadingTheSlot()
    {
        var tally = new DeathTally(_root, "silksong");
        var save = new object();
        Assert.Null(tally.Label);
        Die(tally, save, 2, 500f, "Bone_01", NoBoss);
        tally.Tick(save, 2, 510f, "Bone_01", false, NoBoss);
        Die(tally, save, 2, 520f, "Bone_02", NoBoss);
        Assert.Equal(2, tally.Total);
        Assert.Equal("DEATHS 2", tally.Label);

        // Quit and reload: a fresh tally and a new save object for the same slot.
        var reloaded = new DeathTally(_root, "silksong");
        reloaded.Tick(new object(), 2, 520f, "Tut_01", false, NoBoss);
        Assert.Equal(2, reloaded.Total);
    }

    [Fact]
    public void AnotherSlotAndTheOtherGameKeepTheirOwnCounts()
    {
        var tally = new DeathTally(_root, "silksong");
        var first = new object();
        Die(tally, first, 1, 300f, "A", NoBoss);
        var second = new object();
        tally.Tick(second, 3, 900f, "B", false, NoBoss);
        Assert.Equal(0, tally.Total);
        Die(tally, second, 3, 900f, "B", NoBoss);
        Die(tally, second, 3, 910f, "B", NoBoss);
        Assert.Equal(2, tally.Total);
        tally.Tick(new object(), 1, 300f, "A", false, NoBoss);
        Assert.Equal(1, tally.Total);

        var knight = new DeathTally(_root, "hollow-knight");
        knight.Tick(new object(), 1, 300f, "Town", false, NoBoss);
        Assert.Equal(0, knight.Total);
    }

    [Fact]
    public void BindingWhileDeadDoesNotCountADeath()
    {
        var tally = new DeathTally(_root, "silksong");
        var save = new object();
        tally.Tick(save, 2, 100f, "A", true, NoBoss);
        tally.Tick(save, 2, 100f, "A", true, NoBoss);
        Assert.Equal(0, tally.Total);
    }

    [Fact]
    public void BossStreakGrowsInOneRoomAndEndsWhenLeftAlive()
    {
        var tally = new DeathTally(_root, "hollow-knight");
        var save = new object();
        Die(tally, save, 1, 200f, "Boss_Room", Boss);
        tally.Tick(save, 1, 210f, "Bench_Room", false, NoBoss);   // respawned at the bench
        tally.Tick(save, 1, 220f, "Boss_Room", false, Boss);
        Die(tally, save, 1, 230f, "Boss_Room", Boss);
        Assert.Equal(2, tally.BossDeaths);
        Assert.Equal("DEATHS 2   BOSS 2", tally.Label);

        // Ordinary deaths elsewhere leave the boss streak alone.
        tally.Tick(save, 1, 240f, "Bench_Room", false, NoBoss);
        Die(tally, save, 1, 250f, "Bench_Room", NoBoss);
        Assert.Equal(3, tally.Total);
        Assert.Equal(2, tally.BossDeaths);

        // Beat the boss and walk out alive.
        tally.Tick(save, 1, 260f, "Boss_Room", false, Boss);
        tally.Tick(save, 1, 270f, "Next_Room", false, NoBoss);
        Assert.Equal(0, tally.BossDeaths);
        Assert.Equal("DEATHS 3", tally.Label);
    }

    [Fact]
    public void ADifferentBossStartsANewStreak()
    {
        var tally = new DeathTally(_root, "silksong");
        var save = new object();
        Die(tally, save, 1, 10_000f, "Boss_A", Boss);
        Die(tally, save, 1, 10_010f, "Boss_A", Boss);
        tally.Tick(save, 1, 10_020f, "Boss_B", false, Boss);
        Die(tally, save, 1, 10_030f, "Boss_B", Boss);
        Assert.Equal(1, tally.BossDeaths);
        Assert.Equal("Boss_B", tally.BossScene);
    }

    [Fact]
    public void ANewGameInADeletedSlotStartsFromZero()
    {
        var tally = new DeathTally(_root, "silksong");
        Die(tally, new object(), 4, 5_000f, "A", NoBoss);
        Assert.Equal(1, tally.Total);
        var fresh = new DeathTally(_root, "silksong");
        fresh.Tick(new object(), 4, 3f, "Opening", false, NoBoss);
        Assert.Equal(0, fresh.Total);
        Assert.Contains("total=0", File.ReadAllText(fresh.PathFor(4)));
    }

    [Fact]
    public void NoSaveUnbindsTheLabel()
    {
        var tally = new DeathTally(_root, "silksong");
        var save = new object();
        Die(tally, save, 1, 100f, "A", NoBoss);
        tally.Tick(null, 1, 0f, null, false, NoBoss);
        Assert.Null(tally.Label);
        tally.Tick(save, 0, 0f, "Menu", false, NoBoss);
        Assert.Null(tally.Label);
    }

    [Fact]
    public void AMalformedSidecarReadsAsZero()
    {
        var tally = new DeathTally(_root, "silksong");
        Directory.CreateDirectory(_root);
        File.WriteAllText(tally.PathFor(1), "total=banana\nboss=-3\nnonsense\n");
        tally.Tick(new object(), 1, 100f, "A", false, NoBoss);
        Assert.Equal(0, tally.Total);
        Assert.Equal(0, tally.BossDeaths);
    }
}
