using System.Runtime.InteropServices;

namespace Tinyprobe.Cli.Tests;

public class UpdateCommandTests
{
    [Theory]
    [InlineData("win", Architecture.X64, "tinyprobe-win-x64.zip")]
    [InlineData("linux", Architecture.X64, "tinyprobe-linux-x64.tar.gz")]
    [InlineData("linux", Architecture.Arm64, "tinyprobe-linux-arm64.tar.gz")]
    [InlineData("linux-musl", Architecture.X64, "tinyprobe-linux-musl-x64.tar.gz")]
    [InlineData("linux-musl", Architecture.Arm64, "tinyprobe-linux-musl-arm64.tar.gz")]
    [InlineData("osx", Architecture.Arm64, "tinyprobe-osx-arm64.tar.gz")]
    public void GetAssetName_ReturnsTheReleaseArchive(string os, Architecture architecture, string expected)
    {
        Assert.Equal(expected, UpdateCommand.GetAssetName(os, architecture));
    }

    [Theory]
    [InlineData("osx", Architecture.X64)]    // no Intel macOS build
    [InlineData("win", Architecture.Arm64)]  // the x64 binary is used through emulation
    [InlineData("linux", Architecture.X86)]
    [InlineData(null, Architecture.X64)]
    public void GetAssetName_IsNullWithoutPrebuiltBinary(string? os, Architecture architecture)
    {
        Assert.Null(UpdateCommand.GetAssetName(os, architecture));
    }

    [Theory]
    [InlineData("0.2.0", "0.1.0", true)]
    [InlineData("v0.2.0", "0.1.9", true)]
    [InlineData("0.10.0", "0.9.0", true)]
    [InlineData("1.0.0", "1.0.0-beta.1", true)]
    [InlineData("0.2.0", "0.2.0", false)]
    [InlineData("0.1.0", "0.2.0", false)]
    [InlineData("1.0.0-beta.1", "1.0.0", false)]
    [InlineData("garbage", "0.1.0", false)]
    public void IsNewer_ComparesSemanticVersions(string candidate, string current, bool expected)
    {
        Assert.Equal(expected, UpdateCommand.IsNewer(candidate, current));
    }

    [Theory]
    [InlineData("0.2.0", true, false)]
    [InlineData("v1.2.3", true, false)]
    [InlineData("0.0.0-dev", true, true)]
    [InlineData("1.2.3+abc123", true, false)]
    [InlineData("1.2", false, false)]
    [InlineData("1.2.3.4", false, false)]
    [InlineData("unknown", false, false)]
    public void TryParseVersion_AcceptsSemVer(string value, bool valid, bool prerelease)
    {
        Assert.Equal(valid, UpdateCommand.TryParseVersion(value, out _, out var isPrerelease));
        if (valid)
            Assert.Equal(prerelease, isPrerelease);
    }

    [Fact]
    public void FindChecksum_ReadsSha256SumOutput()
    {
        const string sums = """
            fbd12e76321c8646ffdca3d2c934e00e241886717a4f500225e1c811c4d1f905  tinyprobe-linux-arm64.tar.gz
            eccb566ba438d2cb7c3dcf326ca7b6d50d8e89171cd403162f64cc43f9041e60  tinyprobe-linux-x64.tar.gz
            af8c6ba679c409f62225c9029546a5f76f609538c552b7582e8f0e7377d4cb1d *tinyprobe-win-x64.zip
            """;

        Assert.Equal("eccb566ba438d2cb7c3dcf326ca7b6d50d8e89171cd403162f64cc43f9041e60",
            UpdateCommand.FindChecksum(sums, "tinyprobe-linux-x64.tar.gz"));
        Assert.Equal("af8c6ba679c409f62225c9029546a5f76f609538c552b7582e8f0e7377d4cb1d",
            UpdateCommand.FindChecksum(sums, "tinyprobe-win-x64.zip"));
        Assert.Null(UpdateCommand.FindChecksum(sums, "tinyprobe-osx-arm64.tar.gz"));
    }
}
