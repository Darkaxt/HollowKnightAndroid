using BundleSurgery.Tests.Fixtures;
using Xunit;

namespace BundleSurgery.Tests;

public sealed class SerializedFileTransformerTests
{
    [Fact]
    public void Transform_requires_a_vulkan_slice_when_a_shader_is_present()
    {
        using var fixture = SyntheticAssetFactory.WithShaderPlatforms(15);
        var output = Path.Combine(fixture.OutputRoot, "converted.assets");

        var error = Assert.Throws<InvalidDataException>(() =>
            SerializedFileTransformer.Transform(fixture.InputPath, output, requireVulkan: true));

        Assert.Contains("Vulkan", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Transform_publishes_a_verified_android_serialized_file()
    {
        using var fixture = SyntheticAssetFactory.WithShaderPlatforms(18);
        var output = Path.Combine(fixture.OutputRoot, "converted.assets");

        var report = SerializedFileTransformer.Transform(
            fixture.InputPath,
            output,
            requireVulkan: true);

        Assert.Equal(new TransformReport(1, 1, 0, 24, 13), report);
        Assert.True(File.Exists(output));
        Assert.False(File.Exists(output + ".part"));
        var inspected = SyntheticAssetFactory.Inspect(output);
        Assert.Equal(13u, inspected.TargetPlatform);
        Assert.Equal([18], inspected.ShaderPlatforms);
    }

    [Fact]
    public void Transform_keeps_only_the_vulkan_platform_and_its_blob_slice()
    {
        using var fixture = SyntheticAssetFactory.WithShaderPlatforms(15, 18);
        var output = Path.Combine(fixture.OutputRoot, "converted.assets");

        var report = SerializedFileTransformer.Transform(
            fixture.InputPath,
            output,
            requireVulkan: true);

        Assert.Equal(new TransformReport(1, 1, 0, 24, 13), report);
        var inspected = SyntheticAssetFactory.Inspect(output);
        Assert.Equal([18], inspected.ShaderPlatforms);
        Assert.Equal([2], inspected.CompressedBlob);
    }

    [Fact]
    public void Transform_preserves_every_chunk_from_the_selected_vulkan_platform()
    {
        using var fixture = SyntheticAssetFactory.WithShaderPlatformChunks(
            (15, [1]),
            (18, [2, 3]));
        var output = Path.Combine(fixture.OutputRoot, "converted.assets");

        SerializedFileTransformer.Transform(
            fixture.InputPath,
            output,
            requireVulkan: true);

        var inspected = SyntheticAssetFactory.Inspect(output);
        Assert.Equal([18], inspected.ShaderPlatforms);
        Assert.Equal([2, 3], inspected.CompressedBlob);
        Assert.Equal([0u, 1u], inspected.CompressedOffsets);
    }

    [Fact]
    public void Transform_passes_through_a_missing_vulkan_shader_when_not_required()
    {
        using var fixture = SyntheticAssetFactory.WithShaderPlatforms(15);
        var output = Path.Combine(fixture.OutputRoot, "converted.assets");

        var report = SerializedFileTransformer.Transform(
            fixture.InputPath,
            output,
            requireVulkan: false);

        Assert.Equal(new TransformReport(1, 0, 1, 24, 13), report);
        var inspected = SyntheticAssetFactory.Inspect(output);
        Assert.Equal([15], inspected.ShaderPlatforms);
        Assert.Equal([1], inspected.CompressedBlob);
    }

    [Fact]
    public void Extract_command_recognizes_an_extensionless_serialized_file_by_header()
    {
        using var fixture = SyntheticAssetFactory.WithShaderPlatforms(18);
        var extensionlessInput = Path.Combine(fixture.Root, "level0");
        File.Copy(fixture.InputPath, extensionlessInput);
        var output = Path.Combine(fixture.OutputRoot, "level0.android");

        var exitCode = Program.Main(
            ["extract-vulkan-android", extensionlessInput, output]);

        Assert.Equal(0, exitCode);
        Assert.Equal(13u, SyntheticAssetFactory.Inspect(output).TargetPlatform);
    }

    [Fact]
    public void Shader_report_recognizes_an_extensionless_serialized_file_by_header()
    {
        using var fixture = SyntheticAssetFactory.WithShaderPlatforms(18);
        var extensionlessInput = Path.Combine(fixture.Root, "unity_builtin_extra");
        File.Copy(fixture.InputPath, extensionlessInput);

        var exitCode = Program.Main(["shader-report", extensionlessInput]);

        Assert.Equal(0, exitCode);
    }
}
