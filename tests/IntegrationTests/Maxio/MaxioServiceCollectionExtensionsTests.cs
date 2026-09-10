using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Maxio;

public class MaxioServiceCollectionExtensionsTests
{
    private static IConfiguration Config(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Theory]
    [InlineData("Maxio:ApiKey")]
    [InlineData("Maxio:Subdomain")]
    [InlineData("Maxio:ProductFamilyHandle")]
    public void Registration_fails_fast_when_a_required_setting_is_missing(string missingKey)
    {
        var values = new Dictionary<string, string?>
        {
            ["Maxio:ApiKey"] = "key",
            ["Maxio:Subdomain"] = "site",
            ["Maxio:ProductFamilyHandle"] = "family"
        };
        values[missingKey] = "   "; // blank is not configured

        var ex = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddMaxioSubscriptionBilling(Config(values)));

        Assert.Contains(missingKey, ex.Message);
    }

    [Fact]
    public void Registration_succeeds_when_all_required_settings_are_present()
    {
        var config = Config(new Dictionary<string, string?>
        {
            ["Maxio:ApiKey"] = "key",
            ["Maxio:Subdomain"] = "site",
            ["Maxio:ProductFamilyHandle"] = "family"
        });

        var services = new ServiceCollection();
        services.AddMaxioSubscriptionBilling(config);

        // The billing service is registered.
        Assert.Contains(services, d => d.ServiceType == typeof(ApplicationCore.Interfaces.ISubscriptionBillingService));
    }
}
