using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>Pay-as-you-go metering: what is reported, and what must be refused.</summary>
public class UsageTests
{
    private static FakeBillingProvider WithMeteredComponent() => new FakeBillingProvider()
        .Respond(HttpMethod.Get, "/product_families.json", BillingPayloads.ProductFamilies)
        .Respond(HttpMethod.Get, "/product_families/3023074/components/", BillingPayloads.MeteredComponent);

    [Fact]
    public async Task TheConfiguredComponentIsResolvedAndItsUnitPriceReadAsADecimalNotCents()
    {
        var (client, _) = BillingClientFixture.Create(WithMeteredComponent());

        var component = await client.GetUsageComponentAsync();

        Assert.Equal(3057195, component.Id);
        Assert.Equal("api-call", component.Handle);
        Assert.True(component.IsMetered);
        Assert.Equal("metered_component", component.Kind);
        Assert.Equal(0.01m, component.UnitPrice);
        Assert.Equal("per_unit", component.PricingScheme);
    }

    [Fact]
    public async Task AComponentPricedInCentsIsStillReportedInMajorUnits()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Get, "/product_families.json", BillingPayloads.ProductFamilies)
            .Respond(HttpMethod.Get, "/product_families/3023074/components/",
                """{"component":{"id":1,"handle":"api-call","kind":"metered_component","price_per_unit_in_cents":250}}""");
        var (client, _) = BillingClientFixture.Create(provider);

        var component = await client.GetUsageComponentAsync();

        Assert.Equal(2.50m, component.UnitPrice);
    }

    [Fact]
    public async Task AComponentThatIsNotMeteredIsRefusedBeforeAnyUsageCanBeReported()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Get, "/product_families.json", BillingPayloads.ProductFamilies)
            .Respond(HttpMethod.Get, "/product_families/3023074/components/",
                BillingPayloads.QuantityBasedComponent);
        var (client, _) = BillingClientFixture.Create(provider);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => client.GetUsageComponentAsync());

        Assert.Contains("quantity_based_component", exception.Message, StringComparison.Ordinal);
        Assert.Contains("not metered", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AComponentHandleThatResolvesNowhereIsAConfigurationFault()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Get, "/product_families.json", BillingPayloads.ProductFamilies)
            .Respond(HttpMethod.Get, "/product_families/3023074/components/", "{}", HttpStatusCode.NotFound)
            .Respond(HttpMethod.Get, "/components/lookup.json", "{}", HttpStatusCode.NotFound)
            .Respond(HttpMethod.Get, "/product_families/3023074/components.json", "[]");
        var (client, _) = BillingClientFixture.Create(provider);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => client.GetUsageComponentAsync());

        Assert.Contains("api-call", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheComponentIsAlsoFoundThroughTheSiteWideLookupWhenTheFamilyPathIsUnknown()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Get, "/product_families.json", BillingPayloads.ProductFamilies)
            .Respond(HttpMethod.Get, "/product_families/3023074/components/", "{}", HttpStatusCode.NotFound)
            .Respond(HttpMethod.Get, "/components/lookup.json", BillingPayloads.MeteredComponent);
        var (client, _) = BillingClientFixture.Create(provider);

        var component = await client.GetUsageComponentAsync();

        Assert.Equal(3057195, component.Id);
    }

    [Fact]
    public async Task ReportingUsageSendsTheQuantityAndMemoAgainstTheConfiguredComponent()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Post, "/usages.json", BillingPayloads.UsageRecorded);
        var (client, _) = BillingClientFixture.Create(provider);

        var record = await client.RecordUsageAsync(15236915, 7m, "eShopOnWeb order 42");

        var sent = Assert.Single(provider.Requests);
        Assert.Contains("\"quantity\":7", sent.Body);
        Assert.Contains("\"memo\":\"eShopOnWeb order 42\"", sent.Body);
        Assert.Contains("handle%3Aapi-call", sent.Uri.PathAndQuery, StringComparison.Ordinal);
        Assert.Contains("/subscriptions/15236915/", sent.Uri.PathAndQuery, StringComparison.Ordinal);

        Assert.Equal(138522957, record.Id);
        Assert.Equal(7m, record.Quantity);
        Assert.Equal("eShopOnWeb order 42", record.Memo);
        Assert.Equal("api-call", record.ComponentHandle);
        Assert.Equal(3057195, record.ComponentId);
    }

    [Fact]
    public async Task AQuantityTheProviderReturnsAsAStringIsStillReadAsANumber()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Post, "/usages.json",
                """{"usage":{"id":1,"quantity":"12.5","component_id":3057195}}""");
        var (client, _) = BillingClientFixture.Create(provider);

        var record = await client.RecordUsageAsync(15236915, 12.5m, null);

        Assert.Equal(12.5m, record.Quantity);
    }

    [Fact]
    public async Task ThePeriodToDateBalanceIsReadBackFromTheSubscriptionComponent()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Get, "/subscriptions/15236915/components/3057195.json",
                BillingPayloads.SubscriptionComponent);
        var (client, _) = BillingClientFixture.Create(provider);

        Assert.Equal(250m, await client.GetPeriodToDateUsageAsync(15236915, 3057195));
    }

    [Fact]
    public async Task AProviderThatReportsNoBalanceYieldsNoTotalRatherThanZero()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Get, "/subscriptions/15236915/components/3057195.json",
                """{"component":{"component_id":3057195,"kind":"metered_component"}}""");
        var (client, _) = BillingClientFixture.Create(provider);

        Assert.Null(await client.GetPeriodToDateUsageAsync(15236915, 3057195));
    }

    [Fact]
    public async Task MeteringIsRefusedOutrightWhenNoComponentHandleIsConfigured()
    {
        var settings = BillingClientFixture.Settings();
        settings.MeteredComponentHandle = null;

        var provider = new FakeBillingProvider();
        var (client, _) = BillingClientFixture.Create(provider, settings);

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => client.RecordUsageAsync(15236915, 1m, null));

        Assert.Empty(provider.Requests);
    }
}
