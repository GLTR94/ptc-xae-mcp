using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

public class EsiLocatorTests
{
    private const string XmlWithEsiPath = """
        <TreeItem>
          <EtherCAT>
            <Slave>
              <MdpPlugIn>
                <EsiFile>C:\Program Files (x86)\Beckhoff\TwinCAT\3.1\Config\Io\EtherCAT\Beckhoff EL31xx.xml</EsiFile>
              </MdpPlugIn>
            </Slave>
          </EtherCAT>
        </TreeItem>
        """;

    private const string XmlWithoutEsiPath = """
        <TreeItem>
          <EtherCAT>
            <Slave>
              <Info><VendorId>2</VendorId></Info>
            </Slave>
          </EtherCAT>
        </TreeItem>
        """;

    [Fact]
    public void LocateFromXml_EsiFilePresent_ReturnsPathWhenFileExists()
    {
        const string expectedPath =
            @"C:\Program Files (x86)\Beckhoff\TwinCAT\3.1\Config\Io\EtherCAT\Beckhoff EL31xx.xml";

        string? result = EsiLocator.LocateFromXml(XmlWithEsiPath);

        // If the file exists on this machine the path is returned; if not, null is also acceptable.
        // We can only assert non-null when the file exists — check both outcomes are handled.
        bool fileExists = File.Exists(expectedPath);
        if (fileExists)
            Assert.Equal(expectedPath, result);
        else
            Assert.Null(result);
    }

    [Fact]
    public void LocateFromXml_NoMdpPlugIn_ReturnsNull()
    {
        string? result = EsiLocator.LocateFromXml(XmlWithoutEsiPath);
        Assert.Null(result);
    }

    [Fact]
    public void LocateFromXml_EmptyXml_ReturnsNull()
    {
        Assert.Null(EsiLocator.LocateFromXml(""));
    }

    [Fact]
    public void LocateFromXml_MalformedXml_ReturnsNull()
    {
        Assert.Null(EsiLocator.LocateFromXml("<broken<xml"));
    }

    [Fact]
    public void LocateFromXml_PathToNonExistentFile_ReturnsNull()
    {
        const string xml = """
            <TreeItem>
              <MdpPlugIn>
                <EsiFile>C:\DoesNotExist\NeverHere.xml</EsiFile>
              </MdpPlugIn>
            </TreeItem>
            """;
        Assert.Null(EsiLocator.LocateFromXml(xml));
    }

    [Fact]
    public void FindEsiDirectory_OnMachineWithTwinCAT_ReturnsValidDirectory()
    {
        // This test is environment-dependent: on machines without TwinCAT it returns null.
        string? dir = EsiLocator.FindEsiDirectory();
        if (dir is not null)
            Assert.True(Directory.Exists(dir), $"Returned path does not exist: {dir}");
    }
}
