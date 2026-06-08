using ToastRevival.Agent.Core;
using Xunit;

namespace ToastRevival.Agent.Tests;

// MachineGuid identity (collector phase) — pure normalization of the two registry-read
// machine-identity signals. No registry/filesystem needed; the Registry.GetValue calls
// live in the Windows agent + health service and feed these normalizers.

public class NormalizeMachineGuidTests
{
    [Fact]
    public void Canonical_LowercaseNoBraces_RoundTrips()
        => Assert.Equal("4c4c4544-0042-3010-8052-b9c04f594433",
            MachineIdentity.NormalizeMachineGuid("4c4c4544-0042-3010-8052-b9c04f594433"));

    [Fact]
    public void Uppercase_IsLowercased()
        => Assert.Equal("4c4c4544-0042-3010-8052-b9c04f594433",
            MachineIdentity.NormalizeMachineGuid("4C4C4544-0042-3010-8052-B9C04F594433"));

    [Fact]
    public void Braces_AreStripped()
        => Assert.Equal("4c4c4544-0042-3010-8052-b9c04f594433",
            MachineIdentity.NormalizeMachineGuid("{4c4c4544-0042-3010-8052-b9c04f594433}"));

    [Fact]
    public void SurroundingWhitespace_IsTrimmed()
        => Assert.Equal("4c4c4544-0042-3010-8052-b9c04f594433",
            MachineIdentity.NormalizeMachineGuid("  4c4c4544-0042-3010-8052-b9c04f594433  "));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-guid")]
    [InlineData("12345")]
    [InlineData("00000000-0000-0000-0000-000000000000")] // degenerate empty GUID -> null
    public void MissingOrInvalid_ReturnsNull(string? raw)
        => Assert.Null(MachineIdentity.NormalizeMachineGuid(raw));
}

public class NormalizeHostNameTests
{
    [Fact]
    public void FullHostname_IsPreserved()
        => Assert.Equal("AFN-1004GG11339", MachineIdentity.NormalizeHostName("AFN-1004GG11339"));

    [Fact]
    public void LongerThan15Chars_IsNotTruncated()
    {
        // The whole point of DnsHostName: it carries names the 15-char NetBIOS cap would chop.
        const string full = "AFN-WORKSTATION-ACCOUNTING-07";
        Assert.Equal(full, MachineIdentity.NormalizeHostName(full));
    }

    [Fact]
    public void SurroundingWhitespace_IsTrimmed()
        => Assert.Equal("DESKTOP-K04PCJQ", MachineIdentity.NormalizeHostName("  DESKTOP-K04PCJQ\r\n"));

    [Fact]
    public void Casing_IsPreserved()
        => Assert.Equal("MixedCaseHost", MachineIdentity.NormalizeHostName("MixedCaseHost"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingOrBlank_ReturnsNull(string? raw)
        => Assert.Null(MachineIdentity.NormalizeHostName(raw));

    [Fact]
    public void OverLength_IsCappedAt256()
    {
        var raw = new string('a', 300);
        var result = MachineIdentity.NormalizeHostName(raw);
        Assert.NotNull(result);
        Assert.Equal(256, result!.Length);
    }
}
