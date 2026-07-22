using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class MeteredUsage
{
    [Fact]
    public async Task FindMeteredComponentReportsMeteredKindAndConvertsUnitPriceFromCents()
    {
        var handler = StubHttpMessageHandler.Always(ProviderPayloads.MeteredComponent);
        var client = BillingClientFixture.Create(handler);

        var component = await client.FindMeteredComponentAsync("api-call");

        Assert.NotNull(component);
        Assert.Equal(3057195, component!.Id);
        Assert.Equal("api-call", component.Handle);
        Assert.True(component.IsMetered);
        Assert.Equal("metered_component", component.Kind);
        Assert.Equal("per_unit", component.PricingScheme);
        // One cent per call, not one dollar.
        Assert.Equal(0.01m, component.UnitPrice);
    }

    [Fact]
    public async Task FindMeteredComponentReportsANonMeteredComponentAsNotMetered()
    {
        // UC2 must refuse to meter a quantity-based component; the client reports the kind faithfully so
        // the caller can make that decision.
        var handler = StubHttpMessageHandler.Always(ProviderPayloads.QuantityBasedComponent);
        var client = BillingClientFixture.Create(handler);

        var component = await client.FindMeteredComponentAsync("api-call");

        Assert.NotNull(component);
        Assert.False(component!.IsMetered);
        Assert.Equal("quantity_based_component", component.Kind);
        Assert.Equal(5.00m, component.UnitPrice);
    }

    [Fact]
    public async Task FindMeteredComponentFallsBackToTheFamilyListingWhenTheLookupIsUnavailable()
    {
        var handler = StubHttpMessageHandler.Sequence(
            new StubResponse(HttpStatusCode.NotFound, string.Empty),
            new StubResponse(HttpStatusCode.OK, $"[{ProviderPayloads.MeteredComponent}]"));

        var client = BillingClientFixture.Create(handler);

        var component = await client.FindMeteredComponentAsync("api-call");

        Assert.NotNull(component);
        Assert.Equal(3057195, component!.Id);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task FindMeteredComponentReturnsNullWhenTheHandleResolvesNowhere()
    {
        var handler = StubHttpMessageHandler.Sequence(
            new StubResponse(HttpStatusCode.NotFound, string.Empty),
            new StubResponse(HttpStatusCode.OK, ProviderPayloads.EmptyList));

        var client = BillingClientFixture.Create(handler);

        Assert.Null(await client.FindMeteredComponentAsync("no-such-component"));
    }

    [Fact]
    public async Task RecordUsagePostsTheQuantityAndMemoAgainstTheComponentHandle()
    {
        var handler = StubHttpMessageHandler.Always(ProviderPayloads.UsageRecord, HttpStatusCode.Created);
        var client = BillingClientFixture.Create(handler);

        var receipt = await client.RecordUsageAsync(15236915, "api-call", 5m, "order placed");

        Assert.Equal(900001, receipt.Id);
        Assert.Equal(5m, receipt.Quantity);
        Assert.Equal("order placed", receipt.Memo);
        Assert.Equal(3057195, receipt.ComponentId);
        Assert.Equal(15236915, receipt.SubscriptionId);

        var sent = handler.LastRequest;
        Assert.Equal(HttpMethod.Post, sent.Method);
        // The component is addressed by handle, so a re-seeded numeric id cannot break usage reporting.
        Assert.Contains("handle:api-call", Uri.UnescapeDataString(sent.Uri.AbsolutePath));
        Assert.Contains("\"quantity\":5", sent.Body.Replace(" ", string.Empty));
        Assert.Contains("\"memo\":\"orderplaced\"", sent.Body.Replace(" ", string.Empty));
    }

    [Fact]
    public async Task RecordUsageReadsBackAQuantityTheProviderReturnsAsText()
    {
        // Usage quantity is written as a number but read back as an int-or-string union.
        var handler = StubHttpMessageHandler.Always(ProviderPayloads.UsageRecordWithStringQuantity,
            HttpStatusCode.Created);
        var client = BillingClientFixture.Create(handler);

        var receipt = await client.RecordUsageAsync(15236915, "api-call", 7.5m, null);

        Assert.Equal(7.5m, receipt.Quantity);
    }

    [Fact]
    public async Task RecordUsageSurfacesAProviderRejectionAsATypedException()
    {
        var handler = StubHttpMessageHandler.Always(ProviderPayloads.ValidationErrors,
            HttpStatusCode.UnprocessableEntity);
        var client = BillingClientFixture.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.RecordUsageAsync(15236915, "api-call", 1m, null));

        Assert.Equal(422, exception.StatusCode);
    }

    [Fact]
    public async Task GetComponentUnitBalanceReturnsThePeriodToDateTotal()
    {
        var handler = StubHttpMessageHandler.Always(ProviderPayloads.SubscriptionComponentBalance);
        var client = BillingClientFixture.Create(handler);

        Assert.Equal(42m, await client.GetComponentUnitBalanceAsync(15236915, 3057195));
    }

    [Fact]
    public async Task GetComponentUnitBalanceSurfacesAnUnknownComponentAsATypedException()
    {
        var handler = StubHttpMessageHandler.Always(string.Empty, HttpStatusCode.NotFound);
        var client = BillingClientFixture.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.GetComponentUnitBalanceAsync(15236915, 999999));

        Assert.Equal(404, exception.StatusCode);
    }
}
