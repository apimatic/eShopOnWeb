using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Pay-as-you-go metered usage (UC2): recording units and reading the running period-to-date total.
/// </summary>
public class MaxioBillingClientUsage
{
    private const int SubscriptionId = 88001;
    private const int ComponentId = 3062733;

    [Fact]
    public async Task RecordUsageSendsTheQuantityAndMemoAndMapsTheAcceptedEvent()
    {
        var stub = new MaxioApiStub()
            .Respond(HttpMethod.Post, MaxioApiStub.PathEndingWith("usages.json"), HttpStatusCode.OK,
                MaxioJson.UsageResponse(quantity: 5, memo: "eShopOnWeb order 42"));

        using var harness = new MaxioTestHarness(stub);

        var usage = await harness.Client.RecordUsageAsync(SubscriptionId, ComponentId, 5m, "eShopOnWeb order 42");

        Assert.Equal(991001L, usage.Id);
        Assert.Equal(5m, usage.Quantity);
        Assert.Equal("eShopOnWeb order 42", usage.Memo);
        Assert.Equal(SubscriptionId, usage.SubscriptionId);
        Assert.Equal(ComponentId, usage.ComponentId);
        Assert.Equal("api-call", usage.ComponentHandle);

        var request = Assert.Single(stub.Requests);
        Assert.Contains("\"quantity\"", request.Body, StringComparison.Ordinal);
        Assert.Contains("\"memo\"", request.Body, StringComparison.Ordinal);
        // Both path identifiers must be substituted or the usage lands on the wrong meter.
        Assert.Contains(SubscriptionId.ToString(), request.Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains(ComponentId.ToString(), request.Uri.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecordUsageFallsBackToTheRequestedQuantityWhenTheProviderEchoesNone()
    {
        var stub = new MaxioApiStub()
            .Respond(HttpMethod.Post, MaxioApiStub.PathEndingWith("usages.json"), HttpStatusCode.OK,
                """{ "usage": { "id": 991002, "component_id": 3062733, "subscription_id": 88001 } }""");

        using var harness = new MaxioTestHarness(stub);

        var usage = await harness.Client.RecordUsageAsync(SubscriptionId, ComponentId, 3m, memo: null);

        Assert.Equal(3m, usage.Quantity);
    }

    [Fact]
    public async Task RecordUsageReadsAQuantityTheProviderSendsAsText()
    {
        // The quantity field is a string-or-number union on the wire.
        var stub = new MaxioApiStub()
            .Respond(HttpMethod.Post, MaxioApiStub.PathEndingWith("usages.json"), HttpStatusCode.OK,
                """{ "usage": { "id": 991003, "quantity": "7", "component_id": 3062733, "subscription_id": 88001 } }""");

        using var harness = new MaxioTestHarness(stub);

        var usage = await harness.Client.RecordUsageAsync(SubscriptionId, ComponentId, 7m, memo: null);

        Assert.Equal(7m, usage.Quantity);
    }

    [Fact]
    public async Task RecordUsageSurfacesAValidationFailure()
    {
        var stub = new MaxioApiStub()
            .Respond(HttpMethod.Post, MaxioApiStub.PathEndingWith("usages.json"),
                HttpStatusCode.UnprocessableEntity,
                MaxioJson.ErrorList("Component is not metered."));

        using var harness = new MaxioTestHarness(stub);

        var ex = await Assert.ThrowsAsync<BillingProviderValidationException>(
            () => harness.Client.RecordUsageAsync(SubscriptionId, ComponentId, 1m, null));

        Assert.Contains("Component is not metered.", ex.Errors);
    }

    [Fact]
    public async Task RecordUsageAgainstAnUnknownSubscriptionSurfacesANotFoundFailure()
    {
        // Nothing stubbed: the provider answers 404 for the unknown subscription in the path.
        using var harness = new MaxioTestHarness(new MaxioApiStub());

        var ex = await Assert.ThrowsAsync<BillingProviderNotFoundException>(
            () => harness.Client.RecordUsageAsync(999999, ComponentId, 1m, null));

        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task GetPeriodToDateUnitsReturnsTheAccumulatedUnitBalance()
    {
        var stub = new MaxioApiStub()
            .Respond(HttpMethod.Get, MaxioApiStub.PathContaining("88001", "components", "3062733"),
                HttpStatusCode.OK, MaxioJson.SubscriptionComponentResponse(unitBalance: 17));

        using var harness = new MaxioTestHarness(stub);

        Assert.Equal(17, await harness.Client.GetPeriodToDateUnitsAsync(SubscriptionId, ComponentId));
    }

    [Fact]
    public async Task GetPeriodToDateUnitsReturnsZeroWhenNothingHasBeenUsedThisPeriod()
    {
        var stub = new MaxioApiStub()
            .Respond(HttpMethod.Get, MaxioApiStub.PathContaining("88001", "components", "3062733"),
                HttpStatusCode.OK, MaxioJson.SubscriptionComponentResponse(unitBalance: 0));

        using var harness = new MaxioTestHarness(stub);

        Assert.Equal(0, await harness.Client.GetPeriodToDateUnitsAsync(SubscriptionId, ComponentId));
    }

    [Fact]
    public async Task GetPeriodToDateUnitsReturnsNullWhenTheSubscriptionHasNoLineItemForTheComponent()
    {
        // A 404 here means "nothing used yet", not a failure the caller should see as an error.
        using var harness = new MaxioTestHarness(new MaxioApiStub());

        Assert.Null(await harness.Client.GetPeriodToDateUnitsAsync(SubscriptionId, ComponentId));
    }
}
