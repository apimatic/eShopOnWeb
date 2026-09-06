#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

public class MaxioSettingsTests
{
    private static MaxioSettings Load(params (string Key, string Value)[] values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(pair => pair.Key, pair => (string?)pair.Value))
            .Build();

        return MaxioSettings.Load(configuration);
    }

    [Fact]
    public void Load_BindsTheDocumentedKeys()
    {
        var settings = Load(
            ("Maxio:ApiKey", "key"),
            ("Maxio:Subdomain", "site"),
            ("Maxio:ProductFamilyHandle", "family"),
            ("Maxio:BaseUrl", "https://gateway.example.com"));

        Assert.Equal("key", settings.ApiKey);
        Assert.Equal("site", settings.Subdomain);
        Assert.Equal("family", settings.ProductFamilyHandle);
        Assert.Equal("https://gateway.example.com", settings.BaseUrl);
        Assert.Empty(settings.Validate());
    }

    [Fact]
    public void Validate_RequiresAnApiKeyAndAProductFamily()
    {
        var problems = Load(("Maxio:Subdomain", "site")).Validate();

        Assert.Contains(problems, problem => problem.Contains("Maxio:ApiKey"));
        Assert.Contains(problems, problem => problem.Contains("Maxio:ProductFamilyHandle"));
    }

    [Fact]
    public void Validate_AcceptsABaseUrlInPlaceOfASubdomain()
    {
        var problems = Load(
            ("Maxio:ApiKey", "key"),
            ("Maxio:ProductFamilyHandle", "family"),
            ("Maxio:BaseUrl", "https://gateway.example.com")).Validate();

        Assert.Empty(problems);
    }

    [Fact]
    public void Validate_RequiresEitherASubdomainOrABaseUrl()
    {
        var problems = Load(
            ("Maxio:ApiKey", "key"),
            ("Maxio:ProductFamilyHandle", "family")).Validate();

        Assert.Contains(problems, problem => problem.Contains("Maxio:BaseUrl"));
    }

    [Fact]
    public void Validate_RejectsARelativeBaseUrl()
    {
        var problems = Load(
            ("Maxio:ApiKey", "key"),
            ("Maxio:ProductFamilyHandle", "family"),
            ("Maxio:BaseUrl", "not-a-url")).Validate();

        Assert.Contains(problems, problem => problem.Contains("absolute URL"));
    }

    [Fact]
    public void Validate_RejectsAnUnsupportedPaymentCollectionMethod()
    {
        var problems = Load(
            ("Maxio:ApiKey", "key"),
            ("Maxio:Subdomain", "site"),
            ("Maxio:ProductFamilyHandle", "family"),
            ("Maxio:PaymentCollectionMethod", "carrier-pigeon")).Validate();

        Assert.Contains(problems, problem => problem.Contains("PaymentCollectionMethod"));
    }

    /// <summary>
    /// Subscriptions are additive to the storefront, so missing billing configuration must degrade
    /// that one capability rather than stop the host from starting.
    /// </summary>
    [Fact]
    public async Task AddMaxioSubscriptionBilling_RegistersAWorkingHostWhenNothingIsConfigured()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMaxioSubscriptionBilling(configuration);

        await using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<ISubscriptionService>();

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => service.ListPlansAsync());
        Assert.Contains("Maxio:ApiKey", exception.Message);

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => service.ListSubscriptionsAsync(new SubscriberIdentity("a@b.com", "a@b.com")));
    }
}
