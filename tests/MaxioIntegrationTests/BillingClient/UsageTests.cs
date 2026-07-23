using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.BillingClient;

public class UsageTests
{
    [Fact]
    public async Task FindComponentRecognizesAMeteredComponentByItsKind()
    {
        var handler = new StubHttpMessageHandler()
            .RespondWithJson(MaxioResponses.ProductFamilies)
            .RespondWithJson(MaxioResponses.MeteredComponent);

        var client = BillingClientBuilder.Build(handler);

        var component = await client.FindComponentByHandleAsync("api-call");

        Assert.NotNull(component);
        Assert.True(component.IsMetered);
        Assert.Equal("metered_component", component.Kind);
        Assert.Equal(3062733, component.Id);
        Assert.Equal("per_unit", component.PricingScheme);
    }

    [Fact]
    public async Task FindComponentParsesTheUnitPriceAsWholeCurrencyUnits()
    {
        var handler = new StubHttpMessageHandler()
            .RespondWithJson(MaxioResponses.ProductFamilies)
            .RespondWithJson(MaxioResponses.MeteredComponent);

        var client = BillingClientBuilder.Build(handler);

        var component = await client.FindComponentByHandleAsync("api-call");

        // Component unit prices arrive as a decimal string in dollars, unlike product prices.
        Assert.Equal(0.01m, component!.UnitPrice);
    }

    [Fact]
    public async Task FindComponentReportsANonMeteredComponentAsNotMetered()
    {
        var handler = new StubHttpMessageHandler()
            .RespondWithJson(MaxioResponses.ProductFamilies)
            .RespondWithJson(MaxioResponses.QuantityBasedComponent);

        var client = BillingClientBuilder.Build(handler);

        var component = await client.FindComponentByHandleAsync("api-call");

        Assert.NotNull(component);
        Assert.False(component.IsMetered);
        Assert.Equal("quantity_based_component", component.Kind);
    }

    [Fact]
    public async Task FindComponentResolvesTheFamilyByHandleBeforeAddressingTheComponent()
    {
        var handler = new StubHttpMessageHandler()
            .RespondWithJson(MaxioResponses.ProductFamilies)
            .RespondWithJson(MaxioResponses.MeteredComponent);

        var client = BillingClientBuilder.Build(handler);

        await client.FindComponentByHandleAsync("api-call");

        Assert.Equal("/product_families.json", handler.Requests[0].Path);
        Assert.Equal("/product_families/3026730/components/handle:api-call.json", handler.Requests[1].Path);
    }

    [Fact]
    public async Task FindComponentReturnsNullWhenTheComponentDoesNotExist()
    {
        var handler = new StubHttpMessageHandler()
            .RespondWithJson(MaxioResponses.ProductFamilies)
            .RespondWith(HttpStatusCode.NotFound, string.Empty);

        var client = BillingClientBuilder.Build(handler);

        Assert.Null(await client.FindComponentByHandleAsync("api-call"));
    }

    [Fact]
    public async Task FindComponentFailsLoudlyWhenTheConfiguredFamilyIsMissing()
    {
        var handler = new StubHttpMessageHandler().RespondWithJson(MaxioResponses.EmptyList);
        var client = BillingClientBuilder.Build(handler);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => client.FindComponentByHandleAsync("api-call"));

        Assert.Contains("eshop-subscribe", exception.Message);
    }

    [Fact]
    public async Task RecordUsageSendsTheQuantityAndMemoAgainstTheComponentHandle()
    {
        var handler = new StubHttpMessageHandler().RespondWithJson(MaxioResponses.UsageRecorded);
        var client = BillingClientBuilder.Build(handler);

        await client.RecordUsageAsync(93491347, "api-call", 150, "probe usage");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/subscriptions/93491347/components/handle:api-call/usages.json", request.Path);
        Assert.Contains("\"quantity\":150", request.Body);
        Assert.Contains("\"memo\":\"probe usage\"", request.Body);
    }

    [Fact]
    public async Task RecordUsageMapsTheProvidersAcknowledgement()
    {
        var handler = new StubHttpMessageHandler().RespondWithJson(MaxioResponses.UsageRecorded);
        var client = BillingClientBuilder.Build(handler);

        var usage = await client.RecordUsageAsync(93491347, "api-call", 150, "probe usage");

        Assert.Equal(3633945529L, usage.Id);
        Assert.Equal(93491347, usage.SubscriptionId);
        Assert.Equal("api-call", usage.ComponentHandle);
        Assert.Equal(150m, usage.Quantity);

        // The record itself reports only what this call added, never the running total.
        Assert.Null(usage.PeriodToDateTotal);
    }

    [Fact]
    public async Task PeriodToDateUsageReadsTheUnitBalanceRatherThanTheAllocation()
    {
        var handler = new StubHttpMessageHandler().RespondWithJson(MaxioResponses.SubscriptionComponentWithBalance);
        var client = BillingClientBuilder.Build(handler);

        var total = await client.GetPeriodToDateUsageAsync(93491347, "api-call");

        // allocated_quantity is null for metered components; unit_balance is the real total.
        Assert.Equal(200m, total);
        Assert.Equal("/subscriptions/93491347/components/handle:api-call.json",
            Assert.Single(handler.Requests).Path);
    }

    [Fact]
    public async Task PeriodToDateUsageReturnsNullWhenTheComponentIsNotOnTheSubscription()
    {
        var handler = new StubHttpMessageHandler().RespondWith(HttpStatusCode.NotFound, string.Empty);
        var client = BillingClientBuilder.Build(handler);

        Assert.Null(await client.GetPeriodToDateUsageAsync(93491347, "api-call"));
    }
}
