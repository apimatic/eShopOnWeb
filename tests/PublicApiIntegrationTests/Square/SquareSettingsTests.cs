using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Square.Servers;

namespace PublicApiIntegrationTests.Square;

[TestClass]
public class SquareSettingsTests
{
    private static ValidateOptionsResult Validate(Action<SquareSettings> change)
    {
        var settings = new SquareSettings
        {
            Environment = "sandbox",
            ApplicationId = "app",
            ApplicationSecret = "secret-value-123",
            RedirectUri = "https://shop.test/api/square/callback",
        };
        change(settings);
        return new SquareSettingsValidator().Validate(null, settings);
    }

    [TestMethod]
    public void CompleteSettingsWithoutAnAccessTokenAreValid()
    {
        Assert.IsTrue(Validate(_ => { }).Succeeded, "Square:AccessToken is optional");
    }

    [TestMethod]
    public void EachMissingRequiredSettingIsNamedButNeverEchoed()
    {
        var cases = new (Action<SquareSettings> Change, string Key)[]
        {
            (s => s.Environment = null, "Square:Environment"),
            (s => s.Environment = "staging", "Square:Environment"),
            (s => s.ApplicationId = " ", "Square:ApplicationId"),
            (s => s.ApplicationSecret = "", "Square:ApplicationSecret"),
            (s => s.RedirectUri = null, "Square:RedirectUri"),
            (s => s.RedirectUri = "not a url", "Square:RedirectUri"),
        };
        foreach (var (change, key) in cases)
        {
            var result = Validate(change);
            Assert.IsTrue(result.Failed, key);
            StringAssert.Contains(result.FailureMessage, key);
            Assert.IsFalse(result.FailureMessage.Contains("secret-value-123"));
        }
    }

    [TestMethod]
    public void TheHostRefusesToStartWithoutCredentials()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Square:Environment"] = "sandbox",
        }).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddSquareIntegration(configuration);
        using var provider = services.BuildServiceProvider();

        var ex = Assert.ThrowsException<OptionsValidationException>(() => provider.GetRequiredService<IOptions<SquareSettings>>().Value);
        StringAssert.Contains(ex.Message, "Square:ApplicationId");
    }

    [TestMethod]
    public void EnvironmentNamesMapToSdkEnvironments()
    {
        Assert.IsTrue(SquareSettings.TryParseEnvironment("Sandbox", out var sandbox));
        Assert.AreEqual(ServerEnvironment.Sandbox, sandbox);
        Assert.IsTrue(SquareSettings.TryParseEnvironment("production", out var production));
        Assert.AreEqual(ServerEnvironment.Production, production);
    }

    [TestMethod]
    public void PricesBecomeTheCurrencysSmallestUnit()
    {
        Assert.AreEqual(1950L, SquareMoney.ToMinorUnits(19.5m, "USD"));
        Assert.AreEqual(1200L, SquareMoney.ToMinorUnits(1200m, "JPY"));
        Assert.AreEqual(1234L, SquareMoney.ToMinorUnits(1.234m, "KWD"));
        var ex = Assert.ThrowsException<SquareIntegrationException>(() => SquareMoney.ToMinorUnits(1.005m, "USD"));
        Assert.AreEqual(SquareFailureKind.Rejected, ex.Kind);
    }

    [TestMethod]
    public void SquareEnvironmentVariablesFillTheSquareKeysWithLowestPrecedence()
    {
        var original = Environment.GetEnvironmentVariable("SQUARE_APPLICATION_ID");
        Environment.SetEnvironmentVariable("SQUARE_APPLICATION_ID", "from-env");
        try
        {
            var builder = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Square:Environment"] = "sandbox", ["Square:ApplicationId"] = "explicit" });
            builder.AddSquareEnvironmentVariables();
            var configuration = builder.Build();

            Assert.AreEqual("explicit", configuration["Square:ApplicationId"], "explicit configuration wins");
            Assert.IsTrue(builder.Sources.First() is Microsoft.Extensions.Configuration.Memory.MemoryConfigurationSource);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SQUARE_APPLICATION_ID", original);
        }
    }
}
