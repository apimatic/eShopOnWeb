using System.Collections;
using Microsoft.eShopWeb.SquareCheck.Configuration;
using Microsoft.Extensions.Configuration;
using Square.Servers;

namespace Microsoft.eShopWeb.SquareCheck.UnitTests;

public class SettingsTests
{
    private static SquareSettings Valid() => new()
    {
        Environment = "sandbox",
        ApplicationId = "sandbox-app",
        ApplicationSecret = "top-secret-value",
        RedirectUri = "http://localhost:8080/callback",
    };

    [Fact]
    public void ValidSettings_AreAccepted()
    {
        Assert.True(SquareSettingsValidator.TryValidate(Valid(), out var settings, out var problems));
        Assert.Empty(problems);
        Assert.Equal(ServerEnvironment.Sandbox, settings.Environment);
        Assert.Equal("sandbox", settings.EnvironmentName);
        Assert.Equal("http://localhost:8080/callback", settings.RedirectUri);
        Assert.Equal(8080, settings.RedirectAddress.Port);
    }

    [Theory]
    [InlineData("production")]
    [InlineData(" Production ")]
    public void ProductionIsAccepted(string environment)
    {
        var raw = Valid();
        raw.Environment = environment;
        Assert.True(SquareSettingsValidator.TryValidate(raw, out var settings, out _));
        Assert.Equal(ServerEnvironment.Production, settings.Environment);
    }

    [Fact]
    public void EverythingMissing_NamesEveryKey()
    {
        Assert.False(SquareSettingsValidator.TryValidate(new SquareSettings(), out _, out var problems));
        Assert.Equal(4, problems.Count);
        Assert.Contains(problems, p => p.StartsWith("Square:Environment"));
        Assert.Contains(problems, p => p.StartsWith("Square:ApplicationId"));
        Assert.Contains(problems, p => p.StartsWith("Square:ApplicationSecret"));
        Assert.Contains(problems, p => p.StartsWith("Square:RedirectUri"));
    }

    [Fact]
    public void BlankSecret_IsRefused()
    {
        var raw = Valid();
        raw.ApplicationSecret = "   ";
        Assert.False(SquareSettingsValidator.TryValidate(raw, out _, out var problems));
        Assert.Equal("Square:ApplicationSecret is not set", Assert.Single(problems));
    }

    [Theory]
    [InlineData("custom")]
    [InlineData("staging")]
    public void UnknownEnvironment_IsRefused(string environment)
    {
        var raw = Valid();
        raw.Environment = environment;
        Assert.False(SquareSettingsValidator.TryValidate(raw, out _, out var problems));
        Assert.Contains("Square:Environment must be", Assert.Single(problems));
    }

    [Theory]
    [InlineData("not a url", "is not an absolute URL")]
    [InlineData("https://localhost:8443/callback", "must be an http:// address")]
    [InlineData("http://shop.example.com/callback", "must point at this computer")]
    [InlineData("http://localhost:8080/callback?x=1", "must not carry a query string")]
    public void UnusableRedirect_IsRefused(string redirectUri, string expected)
    {
        var raw = Valid();
        raw.RedirectUri = redirectUri;
        Assert.False(SquareSettingsValidator.TryValidate(raw, out _, out var problems));
        Assert.Contains(expected, Assert.Single(problems));
    }

    [Theory]
    [InlineData("http://127.0.0.1:9000/cb")]
    [InlineData("http://[::1]:9000/cb")]
    public void LoopbackAddresses_AreAccepted(string redirectUri)
    {
        var raw = Valid();
        raw.RedirectUri = redirectUri;
        Assert.True(SquareSettingsValidator.TryValidate(raw, out _, out _));
    }

    [Fact]
    public void Problems_AndToString_NeverContainTheSecret()
    {
        var raw = Valid();
        raw.RedirectUri = "https://example.com";
        SquareSettingsValidator.TryValidate(raw, out _, out var problems);
        Assert.DoesNotContain(problems, p => p.Contains("top-secret-value"));
        Assert.DoesNotContain("top-secret-value", raw.ToString());

        Assert.True(SquareSettingsValidator.TryValidate(Valid(), out var settings, out _));
        Assert.DoesNotContain("top-secret-value", settings.ToString());
    }

    [Fact]
    public void SquareEnvironmentVariables_FeedTheSquareSection()
    {
        var environment = new Hashtable
        {
            ["SQUARE_ENVIRONMENT"] = "sandbox",
            ["SQUARE_APPLICATION_ID"] = "id-from-env",
            ["SQUARE_APPLICATION_SECRET"] = "secret-from-env",
            ["SQUARE_REDIRECT_URI"] = "http://localhost:5555/callback",
        };

        var configuration = SquareConfiguration.Load(environment, includeUserSecrets: false);

        Assert.Equal("sandbox", configuration["Square:Environment"]);
        Assert.Equal("id-from-env", configuration["Square:ApplicationId"]);
        Assert.Equal("secret-from-env", configuration["Square:ApplicationSecret"]);
        Assert.Equal("http://localhost:5555/callback", configuration["Square:RedirectUri"]);
        Assert.True(SquareSettingsValidator.TryValidate(configuration, out var settings, out _));
        Assert.Equal("id-from-env", settings.ApplicationId);
    }

    [Fact]
    public void BlankEnvironmentVariables_DoNotHideOtherSources()
    {
        var mapped = SquareConfiguration.MapSquareVariables(new Hashtable
        {
            ["SQUARE_APPLICATION_ID"] = "  ",
            ["SQUARE_ENVIRONMENT"] = "sandbox",
        }).ToList();

        Assert.Equal(new KeyValuePair<string, string?>("Square:Environment", "sandbox"), Assert.Single(mapped));
    }

    [Fact]
    public void SettingsBindFromTheSquareSection()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Square:Environment"] = "sandbox",
            ["Square:ApplicationId"] = "a",
            ["Square:ApplicationSecret"] = "b",
            ["Square:RedirectUri"] = "http://localhost:1234/cb",
        }).Build();

        Assert.True(SquareSettingsValidator.TryValidate(configuration, out var settings, out _));
        Assert.Equal("a", settings.ApplicationId);
        Assert.Equal("b", settings.ApplicationSecret);
    }
}
