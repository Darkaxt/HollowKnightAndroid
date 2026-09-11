using BundleSurgery;
using Xunit;

public sealed class BridgeSilksongNormalDeathTests
{
    [Fact]
    public void Fresh_rewrite_output_must_match_canonical_identity_before_publication()
    {
        string root = Path.Combine(Path.GetTempPath(), "dualsouls-death-bridge-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string output = Path.Combine(root, "Assembly-CSharp.dll.rewritten");
        try
        {
            File.WriteAllBytes(output, new byte[] { (byte)'M', (byte)'Z', 1, 2, 3 });

            var failure = Assert.Throws<InvalidOperationException>(() =>
                BridgeSilksongNormalDeath.RequireCanonicalOutput(output));

            Assert.Contains("canonical rewritten identity", failure.Message);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
