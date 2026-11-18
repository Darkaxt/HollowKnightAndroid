using DualSouls.Mods;
using Xunit;

public sealed class CollectiblesTests
{
    const string Sample =
        "S|Room_A|100|50\n" +
        "S|Room_B|40|80\n" +
        "P|Room_A|grub|Grub Bottle|25|10\n" +
        "P|Room_A|charm|pd:gotCharm_6|50|25\n" +
        "P|Room_B|charm|pd:gotCharm_6|10|10\n" +
        "P|Room_B|root|pdlist:scenesEncounteredDreamPlantC|20|40\n";

    [Fact]
    public void ParseReadsRoomsAndSpots()
    {
        var t = CollectibleTable.Parse(Sample);
        Assert.Equal(4, t.Spots.Count);
        Assert.True(t.TrySceneSize("Room_B", out float w, out float h));
        Assert.Equal(40f, w); Assert.Equal(80f, h);
        Assert.False(t.TrySceneSize("Room_Z", out _, out _));
        var charm = t.Spots[1];
        Assert.Equal("gotCharm_6", charm.PlayerDataField);
        Assert.Null(charm.PlayerDataList);
        Assert.Equal("scenesEncounteredDreamPlantC", t.Spots[3].PlayerDataList);
        Assert.Null(t.Spots[3].PlayerDataField);
        Assert.Equal("Room_A/grub/Grub Bottle", t.Spots[0].Identity);
    }

    [Fact]
    public void ParseRejectsMalformedLines()
    {
        Assert.Throws<FormatException>(() => CollectibleTable.Parse("P|Room_A|grub|x|1\n"));
        Assert.Throws<FormatException>(() => CollectibleTable.Parse("Q|nonsense\n"));
    }

    [Fact]
    public void RoomOffsetUsesTheGameMapCompassFormula()
    {
        var t = CollectibleTable.Parse(Sample);
        // Room_A is 100x50 tiles; sprite 4x2 units: (25,10) -> (-2 + 1, -1 + 0.4).
        Assert.True(t.TryRoomOffset(t.Spots[0], 4f, 2f, out float x, out float y));
        Assert.Equal(-1f, x, 4);
        Assert.Equal(-0.6f, y, 4);
        Assert.False(t.TryRoomOffset(t.Spots[0], 0f, 2f, out _, out _));
    }

    [Fact]
    public void ChecklistCountsEachItemOnceByAreaAndKind()
    {
        var t = CollectibleTable.Parse(Sample);
        var collected = new HashSet<string> { "pd:gotCharm_6" };
        var areas = CollectibleChecklist.Build(t.Spots, s => collected.Contains(s.Identity),
            scene => scene == "Room_A" ? "Crossroads" : null, out var totals);

        Assert.Equal(1, totals.TotalOf("charm"));          // two placements, one charm
        Assert.Equal(0, totals.MissingOf("charm"));
        Assert.Equal(1, totals.MissingOf("grub"));
        Assert.Equal(1, totals.MissingOf("root"));
        Assert.Equal(new[] { "Crossroads", CollectibleChecklist.OtherArea }, areas.Select(a => a.Name).ToArray());
        Assert.Equal(1, areas[0].Missing);
        Assert.Equal(1, areas[1].MissingOf("root"));
    }

    [Fact]
    public void GeneratedHollowKnightTableParsesWithSizesForEverySpot()
    {
        var t = CollectibleTable.Parse(HollowKnightPatches.Lower.HkCollectibleData.Table);
        Assert.NotEmpty(t.Spots);
        foreach (var s in t.Spots) Assert.True(t.TrySceneSize(s.Scene, out _, out _), s.Scene);
        var byKind = t.Spots.GroupBy(s => s.Kind).ToDictionary(g => g.Key, g => g.Select(s => s.Identity).Distinct().Count());
        Assert.Equal(46, byKind["grub"]);
        Assert.Equal(15, byKind["root"]);
    }

    [Fact]
    public void GeneratedSilksongTableParsesWithSizesForEverySpot()
    {
        var t = CollectibleTable.Parse(SsCollectibleData.Table);
        Assert.NotEmpty(t.Spots);
        foreach (var s in t.Spots) Assert.True(t.TrySceneSize(s.Scene, out _, out _), s.Scene);
        Assert.Contains(t.Spots, s => s.Kind == "flea");
        Assert.Contains(t.Spots, s => s.Kind == "relic");
    }
}
