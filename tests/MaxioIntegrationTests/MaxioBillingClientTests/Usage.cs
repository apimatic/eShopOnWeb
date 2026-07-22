using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class Usage
{
    private readonly StubHttpMessageHandler _handler = new();

    [Fact]
    public async Task RecordsUsageAgainstTheConfiguredMeteredComponent()
    {
        _handler.WithMeteredComponent()
            .RespondOk(HttpMethod.Post, "/usages.json", MaxioJson.UsageResponse(900, 42, 5, "five calls"));
        var client = BillingClientBuilder.Build(_handler);

        var usage = await client.RecordUsageAsync(42, 5m, "five calls");

        Assert.Equal(900, usage.Id);
        Assert.Equal(42, usage.SubscriptionId);
        Assert.Equal(5m, usage.Quantity);
        Assert.Equal("five calls", usage.Memo);
        Assert.Equal("api-call", usage.ComponentHandle);
    }

    [Fact]
    public async Task SendsTheQuantityAndMemoToTheProvider()
    {
        _handler.WithMeteredComponent()
            .RespondOk(HttpMethod.Post, "/usages.json", MaxioJson.UsageResponse(900, 42, 3, "order 17"));
        var client = BillingClientBuilder.Build(_handler);

        await client.RecordUsageAsync(42, 3m, "order 17");

        var posted = _handler.RequestsFor("/usages.json").Single(request => request.Method == HttpMethod.Post);
        Assert.Contains("\"quantity\":3", posted.Body);
        Assert.Contains("order 17", posted.Body);

        // The component is addressed by its resolved numeric id on the subscription's usage path.
        Assert.Contains($"/subscriptions/42/components/{BillingClientBuilder.MeteredComponentId}/usages.json",
            posted.Path);
    }

    [Fact]
    public async Task RefusesToRecordUsageAgainstANonMeteredComponent()
    {
        // A component of the wrong kind is a seeding mistake that cannot be fixed in place, so it
        // must be reported as a configuration problem and nothing may be billed.
        _handler.WithMeteredComponent(kind: "quantity_based_component");
        var client = BillingClientBuilder.Build(_handler);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => client.RecordUsageAsync(42, 1m, null));

        Assert.Contains("not metered", exception.Message);
        Assert.Empty(_handler.RequestsFor("/usages.json"));
    }

    [Fact]
    public async Task RefusesToRecordUsageWhenTheComponentHandleDoesNotResolve()
    {
        _handler
            .RespondOk(HttpMethod.Get, "/product_families.json",
                MaxioJson.ProductFamilies((BillingClientBuilder.ProductFamilyId, BillingClientBuilder.ProductFamilyHandle)))
            .Respond(HttpMethod.Get, "/components/handle:api-call", HttpStatusCode.NotFound, MaxioJson.NotFound());
        var client = BillingClientBuilder.Build(_handler);

        await Assert.ThrowsAsync<BillingConfigurationException>(() => client.RecordUsageAsync(42, 1m, null));
        Assert.Empty(_handler.RequestsFor("/usages.json"));
    }

    [Fact]
    public async Task RefusesToRecordUsageWhenTheProductFamilyHandleDoesNotResolve()
    {
        _handler.RespondOk(HttpMethod.Get, "/product_families.json",
            MaxioJson.ProductFamilies((1, "some-other-family")));
        var client = BillingClientBuilder.Build(_handler);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => client.RecordUsageAsync(42, 1m, null));

        Assert.Contains(BillingClientBuilder.ProductFamilyHandle, exception.Message);
    }

    [Fact]
    public async Task ResolvesTheCatalogOnceAndReusesIt()
    {
        _handler.WithMeteredComponent()
            .RespondOk(HttpMethod.Post, "/usages.json", MaxioJson.UsageResponse(900, 42, 1));
        var client = BillingClientBuilder.Build(_handler);

        await client.RecordUsageAsync(42, 1m, null);
        await client.RecordUsageAsync(42, 1m, null);

        Assert.Single(_handler.RequestsFor("/product_families.json"));
        Assert.Single(_handler.RequestsFor("/components/handle:api-call"));
    }

    [Fact]
    public async Task SurfacesAProviderRejectionOfUsageWithItsOwnMessage()
    {
        _handler.WithMeteredComponent()
            .Respond(HttpMethod.Post, "/usages.json", HttpStatusCode.UnprocessableEntity,
                MaxioJson.ErrorList("Quantity: must be a number."));
        var client = BillingClientBuilder.Build(_handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.RecordUsageAsync(42, 1m, null));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("Quantity: must be a number.", exception.ProviderMessage);
    }

    [Fact]
    public async Task SumsThePeriodToDateTotalAcrossUsageRecords()
    {
        _handler.WithMeteredComponent()
            .RespondOk(HttpMethod.Get, "/usages.json", MaxioJson.UsageList(3, 4, 5));
        var client = BillingClientBuilder.Build(_handler);

        var total = await client.GetUsageTotalAsync(42, null);

        Assert.Equal(12m, total);
    }

    [Fact]
    public async Task SumsQuantitiesSentAsStringsAsWellAsNumbers()
    {
        // The provider may serialise a usage quantity either way.
        _handler.WithMeteredComponent()
            .RespondOk(HttpMethod.Get, "/usages.json", MaxioJson.UsageList(2, "3", 4));
        var client = BillingClientBuilder.Build(_handler);

        Assert.Equal(9m, await client.GetUsageTotalAsync(42, null));
    }

    [Fact]
    public async Task ReturnsZeroWhenNoUsageHasBeenRecorded()
    {
        _handler.WithMeteredComponent().RespondOk(HttpMethod.Get, "/usages.json", "[]");
        var client = BillingClientBuilder.Build(_handler);

        Assert.Equal(0m, await client.GetUsageTotalAsync(42, null));
    }

    [Fact]
    public async Task FollowsPaginationSoTheTotalIsNeverSilentlyShort()
    {
        // A full page means there may be more. Stopping there would under-report the customer's
        // bill, so the client must keep paging until a short page arrives.
        var fullPage = MaxioJson.UsageList(Enumerable.Repeat((object)1, 200).ToArray());
        var lastPage = MaxioJson.UsageList(7);

        _handler.WithMeteredComponent()
            .RespondInSequence(HttpMethod.Get, "/usages.json", fullPage, lastPage);
        var client = BillingClientBuilder.Build(_handler);

        var total = await client.GetUsageTotalAsync(42, null);

        Assert.Equal(207m, total);
        Assert.Equal(2, _handler.RequestsFor("/usages.json").Count);
    }

    [Fact]
    public async Task BoundsTheTotalToTheCurrentPeriodWhenAStartDateIsGiven()
    {
        _handler.WithMeteredComponent().RespondOk(HttpMethod.Get, "/usages.json", MaxioJson.UsageList(1));
        var client = BillingClientBuilder.Build(_handler);

        await client.GetUsageTotalAsync(42, new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Contains("since_date", _handler.LastRequest.Query);
    }

    [Fact]
    public async Task ReadsTheUnitPriceInWholeCurrencyUnits()
    {
        // The provider reports unit_price as a decimal string in dollars, not cents.
        _handler.WithMeteredComponent(unitPrice: "0.01");
        var client = BillingClientBuilder.Build(_handler);

        Assert.Equal(0.01m, await client.GetUsageUnitPriceAsync());
    }

    [Fact]
    public async Task ReadsAUnitPriceWithMoreThanTwoDecimalPlaces()
    {
        _handler.WithMeteredComponent(unitPrice: "0.0025");
        var client = BillingClientBuilder.Build(_handler);

        Assert.Equal(0.0025m, await client.GetUsageUnitPriceAsync());
    }
}
