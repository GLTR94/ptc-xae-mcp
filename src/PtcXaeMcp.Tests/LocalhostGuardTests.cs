using PtcXaeMcp.WinForms.Security;

namespace PtcXaeMcp.Tests;

public class LocalhostGuardTests
{
    private const int Port = 5010;

    [Theory]
    [InlineData("localhost:5010")]
    [InlineData("127.0.0.1:5010")]
    [InlineData("[::1]:5010")]
    [InlineData("LOCALHOST:5010")]
    public void IsAllowedHost_LoopbackOnPort_ReturnsTrue(string host)
        => Assert.True(LocalhostGuard.IsAllowedHost(host, Port));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("evil.example")]
    [InlineData("evil.example:5010")]
    [InlineData("localhost:5011")]
    [InlineData("localhost")]
    [InlineData("192.168.1.5:5010")]
    public void IsAllowedHost_NotLoopbackOrWrongPort_ReturnsFalse(string? host)
        => Assert.False(LocalhostGuard.IsAllowedHost(host, Port));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void IsAllowedOrigin_Absent_ReturnsTrue(string? origin)
        => Assert.True(LocalhostGuard.IsAllowedOrigin(origin));

    [Theory]
    [InlineData("http://localhost:5010")]
    [InlineData("http://127.0.0.1")]
    [InlineData("https://localhost")]
    [InlineData("http://[::1]:5010")]
    public void IsAllowedOrigin_HttpLoopback_ReturnsTrue(string origin)
        => Assert.True(LocalhostGuard.IsAllowedOrigin(origin));

    [Theory]
    [InlineData("http://evil.example")]
    [InlineData("https://evil.example:5010")]
    [InlineData("null")] // browsers send the literal string "null" for some cross-origin cases
    [InlineData("file://localhost/etc/passwd")]
    [InlineData("not a uri")]
    public void IsAllowedOrigin_NotHttpLoopback_ReturnsFalse(string origin)
        => Assert.False(LocalhostGuard.IsAllowedOrigin(origin));

    [Fact]
    public void IsAuthorized_NoTokenConfigured_AlwaysTrue()
    {
        Assert.True(LocalhostGuard.IsAuthorized(null, null));
        Assert.True(LocalhostGuard.IsAuthorized("Bearer wrong", ""));
        Assert.True(LocalhostGuard.IsAuthorized("garbage", null));
    }

    [Fact]
    public void IsAuthorized_TokenConfigured_RequiresMatchingBearerHeader()
    {
        const string token = "s3cr3t-token";

        Assert.True(LocalhostGuard.IsAuthorized($"Bearer {token}", token));
        Assert.False(LocalhostGuard.IsAuthorized(null, token));
        Assert.False(LocalhostGuard.IsAuthorized("", token));
        Assert.False(LocalhostGuard.IsAuthorized("Bearer wrong-token", token));
        Assert.False(LocalhostGuard.IsAuthorized(token, token)); // missing "Bearer " prefix
        Assert.False(LocalhostGuard.IsAuthorized($"bearer {token}", token)); // case-sensitive prefix
    }

    [Fact]
    public void AuthRequired_ReflectsEnvironmentVariable()
    {
        string? previous = Environment.GetEnvironmentVariable(LocalhostGuard.TokenEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(LocalhostGuard.TokenEnvVar, null);
            Assert.False(LocalhostGuard.AuthRequired());

            Environment.SetEnvironmentVariable(LocalhostGuard.TokenEnvVar, "some-token");
            Assert.True(LocalhostGuard.AuthRequired());
        }
        finally
        {
            Environment.SetEnvironmentVariable(LocalhostGuard.TokenEnvVar, previous);
        }
    }
}
