using FluentAssertions;
using Xunit;

namespace AutomaticLanguageSwitching.NativeHost.Tests;

public sealed class KeyboardLayoutRulesTests
{
    [Theory]
    [InlineData("04090409", "00000409")]
    [InlineData("00000409", "00000409")]
    [InlineData("F0A80422", "00000422")]
    public void TryNormalizeToStrictStableKlid_Normalizes_To_Strict_Klid(string input, string expected)
    {
        KeyboardLayoutRules.TryNormalizeToStrictStableKlid(input).Should().Be(expected);
    }

    [Fact]
    public void ResolveStableLayoutCandidate_Rejects_Transient_Unmappable_Value()
    {
        var resolution = KeyboardLayoutRules.ResolveStableLayoutCandidate(
            "storage",
            "F0A80422",
            ["00000409"],
            ["00000409"]);

        resolution.LayoutId.Should().BeNull();
        resolution.Source.Should().Be("storage:transient-or-non-normalized");
    }

    [Fact]
    public void ResolveStableLayoutCandidate_Resolves_Transient_Value_To_Normalized_Stable_Klid()
    {
        var resolution = KeyboardLayoutRules.ResolveStableLayoutCandidate(
            "storage",
            "F0A80422",
            ["00000409", "00000422"],
            ["F0A80422", "04090409"]);

        resolution.LayoutId.Should().Be("00000422");
        resolution.Source.Should().Be("storage:stable-canonical");
        resolution.LayoutId.Should().NotBe("F0A80422");
    }

    [Fact]
    public void FinalizeStableLayoutId_Never_Allows_NonNormalized_Raw_Value_To_Survive()
    {
        var finalized = KeyboardLayoutRules.FinalizeStableLayoutId(
            "04090409",
            ["00000409"],
            ["00000409"]);

        finalized.Should().Be("00000409");
        finalized.Should().NotBe("04090409");
    }

    [Fact]
    public void TryGetStableLayoutIdForStorage_Accepts_Stable_And_Rejects_Unmappable()
    {
        var service = new KeyboardLayoutService(
            () => ["00000409", "00000422"],
            () => ["00000409", "00000422"]);

        service.TryGetStableLayoutIdForStorage("04090409").Should().Be("00000409");
        service.TryGetStableLayoutIdForStorage("F0A80422").Should().Be("00000422");
        service.TryGetStableLayoutIdForStorage("XYZ").Should().BeNull();
    }

    [Fact]
    public void TryGetStableLayoutIdForStorage_Skips_Overwrite_When_No_Stable_Klid_Can_Be_Derived()
    {
        var service = new KeyboardLayoutService(
            () => ["00000409"],
            () => ["00000409"]);

        service.TryGetStableLayoutIdForStorage("F0A80422").Should().BeNull();
    }
}
