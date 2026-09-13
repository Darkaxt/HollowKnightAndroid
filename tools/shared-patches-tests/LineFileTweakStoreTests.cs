using DualSouls.Mods;
using Xunit;

public sealed class LineFileTweakStoreTests
{
    [Fact]
    public void KotlinCompatibleFileRoundTripsNamespacedValuesInStableOrder()
    {
        string root = Path.Combine(Path.GetTempPath(), "dualsouls-store-" + Guid.NewGuid());
        string file = LineFileTweakStore.ProfilePath(root, "hollow-knight");
        try
        {
            var store = new LineFileTweakStore(file);
            store.Write("dualsouls.mods.hollow-knight.value.damage_received", "no_mask_loss");
            store.Write("dualsouls.mods.hollow-knight.master", "1");
            store.Flush();

            Assert.Equal(
                "dualsouls.mods.hollow-knight.master=1\n" +
                "dualsouls.mods.hollow-knight.value.damage_received=no_mask_loss\n",
                File.ReadAllText(file).Replace("\r\n", "\n"));
            Assert.Equal("1", new LineFileTweakStore(file).Read("dualsouls.mods.hollow-knight.master"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void MalformedFileFailsClosedToMissingValues()
    {
        string file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "not-a-state-line\n");
            var store = new LineFileTweakStore(file);

            Assert.Null(store.Read("dualsouls.mods.hollow-knight.master"));
            Assert.Null(store.Read("dualsouls.mods.hollow-knight.value.damage_received"));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void ProfilePathRejectsTraversalAndSeparatesGames()
    {
        string root = Path.Combine(Path.GetTempPath(), "persistent");

        Assert.EndsWith(
            Path.Combine("profiles", "hollow-knight", "mods", "builtin-state.txt"),
            LineFileTweakStore.ProfilePath(root, "hollow-knight"));
        Assert.NotEqual(
            LineFileTweakStore.ProfilePath(root, "hollow-knight"),
            LineFileTweakStore.ProfilePath(root, "silksong"));
        Assert.Throws<ArgumentException>(() => LineFileTweakStore.ProfilePath(root, "../escape"));
    }
}
