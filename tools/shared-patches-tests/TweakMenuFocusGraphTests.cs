using DualSouls.Mods;
using Xunit;

namespace SharedPatches.Tests;

public sealed class TweakMenuFocusGraphTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(11)]
    public void DownTraversalVisitsCategoryEveryRowResetAndBackExactlyOnce(int rowCount)
    {
        var visited = new List<TweakMenuFocusTarget>();
        TweakMenuFocusTarget current = TweakMenuFocusTarget.Group;

        do
        {
            Assert.DoesNotContain(current, visited);
            visited.Add(current);
            current = TweakMenuFocusGraph.Move(current, 1, rowCount);
        }
        while (current != TweakMenuFocusTarget.Group);

        Assert.Equal(rowCount + 3, visited.Count);
        Assert.Equal(TweakMenuFocusTarget.Group, visited[0]);
        for (int row = 0; row < rowCount; row++)
            Assert.Equal(TweakMenuFocusTarget.Row(row), visited[row + 1]);
        Assert.Equal(TweakMenuFocusTarget.Reset, visited[^2]);
        Assert.Equal(TweakMenuFocusTarget.Back, visited[^1]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(11)]
    public void UpTraversalIsTheExactInverseOfDownTraversal(int rowCount)
    {
        TweakMenuFocusTarget current = TweakMenuFocusTarget.Group;
        for (int step = 0; step < rowCount + 3; step++)
        {
            TweakMenuFocusTarget down = TweakMenuFocusGraph.Move(current, 1, rowCount);
            Assert.Equal(current, TweakMenuFocusGraph.Move(down, -1, rowCount));
            current = down;
        }
        Assert.Equal(TweakMenuFocusTarget.Group, current);
    }

    [Fact]
    public void RowTargetRequiresANonnegativeIndex()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TweakMenuFocusTarget.Row(-1));
    }
}
