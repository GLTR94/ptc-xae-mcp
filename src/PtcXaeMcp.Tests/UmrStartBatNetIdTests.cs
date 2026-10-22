using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Issue #4: <c>twincat_umr_start</c> must honour a fixed NetId in the instance's own <c>Start.bat</c>
/// (<c>-i "192.168.250.20.1.1"</c>) instead of always launching with <c>-i path</c>, and report the effective
/// NetId. Pure parsing — no UMR, no files.
/// </summary>
public class UmrStartBatNetIdTests
{
    private const string DefaultStartBat = """
        @echo off

        pushd "%~dp0" || exit /b 1

        for %%I in (.) do set TC_INST_NAME=%%~nxI
        start "%TC_INST_NAME%" /min "%TWINCAT3DIR%Runtimes\bin\TcSystemServiceUm.exe" -t bin -i path -n "%TC_INST_NAME%" -c .\3.1

        popd
        """;

    [Fact]
    public void QuotedFixedNetId_IsReturned()
    {
        const string bat = "@echo off\r\n\r\nfor %%I in (.) do set TC_INST_NAME=%%~nxI\r\n" +
            "start \"%TC_INST_NAME%\" /min \"%TWINCAT3DIR%Runtimes\\bin\\TcSystemServiceUm.exe\" -t bin -i \"192.168.250.20.1.1\" -n \"%TC_INST_NAME%\" -c .\\3.1\r\n";

        Assert.Equal("192.168.250.20.1.1", UmrProcessManager.ParseStartBatNetId(bat));
    }

    [Fact]
    public void UnquotedFixedNetId_IsReturned()
    {
        const string bat = "start \"x\" /min TcSystemServiceUm.exe -t bin -i 10.0.0.5.1.1 -n \"x\" -c .\\3.1";

        Assert.Equal("10.0.0.5.1.1", UmrProcessManager.ParseStartBatNetId(bat));
    }

    [Fact]
    public void IPath_ReturnsNull()
    {
        Assert.Null(UmrProcessManager.ParseStartBatNetId(DefaultStartBat));
    }

    [Theory]
    [InlineData("")]
    [InlineData("@echo off\r\necho nothing to start here\r\n")]
    [InlineData("start TcSystemServiceUm.exe -t bin -n \"x\" -c .\\3.1")]
    public void MissingLineOrArgument_ReturnsNull(string bat)
    {
        Assert.Null(UmrProcessManager.ParseStartBatNetId(bat));
    }

    [Theory]
    [InlineData("start TcSystemServiceUm.exe -t bin -i \"%MY_NETID%\" -n \"x\" -c .\\3.1")]
    [InlineData("start TcSystemServiceUm.exe -t bin -i 192.168.1.1 -n \"x\" -c .\\3.1")]
    public void NonLiteralOrMalformedNetId_ReturnsNull(string bat)
    {
        Assert.Null(UmrProcessManager.ParseStartBatNetId(bat));
    }

    [Fact]
    public void CommentedOutLine_IsIgnored()
    {
        const string bat = "rem start TcSystemServiceUm.exe -t bin -i 1.2.3.4.1.1 -n \"x\" -c .\\3.1\r\n" +
            ":: start TcSystemServiceUm.exe -t bin -i 5.6.7.8.1.1 -n \"x\" -c .\\3.1\r\n" +
            "start TcSystemServiceUm.exe -t bin -i path -n \"x\" -c .\\3.1\r\n";

        Assert.Null(UmrProcessManager.ParseStartBatNetId(bat));
    }

    [Fact]
    public void RegistryNetId_IsDecodedFromHex()
    {
        const string xml = """
            <Key Name="System">
                <Value Name="AmsNetId" Type="BIN">C0A8FA140101</Value>
            </Key>
            """;

        Assert.Equal("192.168.250.20.1.1", UmrProcessManager.ParseRegistryNetId(xml));
    }

    [Theory]
    [InlineData("<Key Name=\"System\"></Key>")]
    [InlineData("<Value Name=\"AmsNetId\" Type=\"BIN\">C0A8</Value>")]
    public void RegistryNetId_MissingOrMalformed_ReturnsNull(string xml)
    {
        Assert.Null(UmrProcessManager.ParseRegistryNetId(xml));
    }
}
