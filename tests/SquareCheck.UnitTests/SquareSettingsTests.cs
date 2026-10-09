using System.Collections;
using Microsoft.eShopWeb.SquareCheck.Configuration;
using Microsoft.Extensions.Configuration;
using Square.Servers;
using Xunit;

namespace Microsoft.eShopWeb.SquareCheck.UnitTests;

public sealed class SquareSettingsTests
{
    private const string Secret = "sandbox-sq0csb-TEST-SECRET-NOT-REAL";

    private static IConfiguration Config(
        string? environment = "sandbox",
        string? applicationId = "sandbox-sq0idb-TESTAPP",
        string? secret = Secret,
        string? redirect = "http://localhost:8080/callback") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Square:Environment"] = environment,
            ["Square:ApplicationId"] = applicationId,
            ["Square:ApplicationSecret"] = secret,
            ["Square:RedirectUri"] = redirect,
        }).Build();

    [Fact]
    public void BindsTheSquareSection()
    {
        var settings = SquareSettings.Load(Config()).Settings;

        Assert.NotNull(settings);
        Assert.Equal(ServerEnvironment.Sandbox, settings.Environment);
        Assert.Equal("sandbox-sq0idb-TESTAPP", settings.ApplicationId);
        Assert.Equal(Secret, settings.ApplicationSecret);
        Assert.Equal(new Uri("http://localhost:8080/callback"), settings.RedirectUri);
    }

    [Theory]
    [InlineData("Sandbox")]
    [InlineData(" sandbox ")]
    public void EnvironmentIsCaseAndSpaceTolerant(string environment)
    {
        Assert.Equal(ServerEnvironment.Sandbox, SquareSettings.Load(Config(environment: environment)).Settings!.Environment);
    }

    [Fact]
    public void ProductionIsAccepted()
    {
        Assert.Equal(ServerEnvironment.Production, SquareSettings.Load(Config(environment: "production")).Settings!.Environment);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankValuesAreRefused_NamingTheKey_NotTheValue(string? blank)
    {
        var result = SquareSettings.Load(Config(applicationId: blank, secret: blank));

        Assert.Null(result.Settings);
        var error = Assert.Single(result.Errors);
        Assert.Contains("Square:ApplicationId", error);
        Assert.Contains("Square:ApplicationSecret", error);
    }

    [Theory]
    [InlineData("staging")]
    [InlineData("custom")]
    public void UnknownEnvironmentIsRefused(string environment)
    {
        var result = SquareSettings.Load(Config(environment: environment));

        Assert.Null(result.Settings);
        Assert.Contains("Square:Environment must be 'sandbox' or 'production'.", result.Errors);
    }

    [Theory]
    [InlineData("callback")]
    [InlineData("https://localhost:8080/callback")]
    [InlineData("http://example.com/callback")]
    [InlineData("http://192.168.1.10:8080/callback")]
    public void RedirectMustBeAPlainHttpAddressOnThisComputer(string redirect)
    {
        var result = SquareSettings.Load(Config(redirect: redirect));

        Assert.Null(result.Settings);
        Assert.Contains(result.Errors, e => e.StartsWith("Square:RedirectUri", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("http://127.0.0.1:8080/callback")]
    [InlineData("http://[::1]:8080/callback")]
    [InlineData("http://LOCALHOST:9000/")]
    public void LoopbackRedirectsAreAccepted(string redirect)
    {
        Assert.NotNull(SquareSettings.Load(Config(redirect: redirect)).Settings);
    }

    [Fact]
    public void ToStringNeverIncludesTheSecret()
    {
        var settings = SquareSettings.Load(Config()).Settings!;

        Assert.DoesNotContain(Secret, settings.ToString());
    }

    [Fact]
    public void TheDocumentedEnvironmentVariablesMapOntoTheSquareKeys()
    {
        IDictionary environment = new Hashtable
        {
            ["SQUARE_ENVIRONMENT"] = "sandbox",
            ["SQUARE_APPLICATION_ID"] = "app",
            ["SQUARE_APPLICATION_SECRET"] = "secret",
            ["SQUARE_REDIRECT_URI"] = "http://localhost:8080/callback",
            ["SQUARE_ACCESS_TOKEN"] = "must-not-be-used",
        };

        var mapped = SquareConfiguration.MapAliases(environment).ToDictionary(kv => kv.Key, kv => kv.Value);

        Assert.Equal(
            new Dictionary<string, string?>
            {
                ["Square:Environment"] = "sandbox",
                ["Square:ApplicationId"] = "app",
                ["Square:ApplicationSecret"] = "secret",
                ["Square:RedirectUri"] = "http://localhost:8080/callback",
            },
            mapped);
    }
}
