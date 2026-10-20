using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSettingsTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value))
            .Build();

    private static readonly (string, string?)[] Valid =
    {
        ("Maxio:ApiKey", "k"), ("Maxio:Subdomain", "s"), ("Maxio:ProductFamilyHandle", "f")
    };

    [Theory]
    [InlineData("Maxio:ApiKey")]
    [InlineData("Maxio:Subdomain")]
    [InlineData("Maxio:ProductFamilyHandle")]
    public void StartupFailsNamingTheKeyWhenAnyRequiredPartIsMissingOrBlank(string blankKey)
    {
        var values = Valid.Select(v => v.Item1 == blankKey ? (v.Item1, (string?)"  ") : v).ToArray();

        var ex = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddMaxioBilling(Config(values)));

        Assert.Contains(blankKey, ex.Message);
    }

    [Fact]
    public void StartupFailsWhenNothingIsConfigured()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddMaxioBilling(Config()));

        Assert.Contains("Maxio:ApiKey", ex.Message);
        Assert.Contains("Maxio:Subdomain", ex.Message);
        Assert.Contains("Maxio:ProductFamilyHandle", ex.Message);
    }

    [Fact]
    public void ErrorMessagesNeverEchoTheValues()
    {
        var bad = Config(("Maxio:ApiKey", "super-secret-value"), ("Maxio:Subdomain", "s"), ("Maxio:ProductFamilyHandle", "f"),
            ("Maxio:BaseUrl", "not a url"));

        var ex = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddMaxioBilling(bad));

        Assert.Contains("Maxio:BaseUrl", ex.Message);
        Assert.DoesNotContain("super-secret-value", ex.Message);
        Assert.DoesNotContain("not a url", ex.Message);
    }

    [Fact]
    public void ValidConfigurationRegistersWithAndWithoutBaseUrl()
    {
        new ServiceCollection().AddMaxioBilling(Config(Valid));
        new ServiceCollection().AddMaxioBilling(Config(Valid.Append(("Maxio:BaseUrl", "http://localhost:5000/")).ToArray()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("us")]
    [InlineData("US")]
    [InlineData("eu")]
    public void EnvironmentParses(string? value) => MaxioSettings.ParseEnvironment(value);

    [Fact]
    public void UnknownEnvironmentFailsStartup()
    {
        Assert.Throws<InvalidOperationException>(() => MaxioSettings.ParseEnvironment("mars"));
        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddMaxioBilling(Config(Valid.Append(("MAXIO_ENVIRONMENT", "mars")).ToArray())));
    }
}
