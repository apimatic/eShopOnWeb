using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Pay-as-you-go usage (plan.md UC2): recording units, the metered-kind guard, and the period-to-date
/// total including its documented fallbacks.
/// </summary>
public class MaxioBillingClientUsageTests
{
    private static FakeMaxioHandler WithComponent(FakeMaxioHandler handler) =>
        handler.EnqueueOk(MaxioPayloads.ComponentResponse());

    [Fact]
    public async Task RecordUsageAsync_PostsTheQuantityAndMemo_AndMapsTheAcceptedEvent()
    {
        var handler = WithComponent(new FakeMaxioHandler())
            .EnqueueOk(MaxioPayloads.UsageResponse(quantity: 5, memo: "eShopOnWeb order 42"));

        var (client, _) = TestClientFactory.Create(handler);

        var usage = await client.RecordUsageAsync(60001, 5m, "eShopOnWeb order 42");

        Assert.Equal(900001L, usage.Id);
        Assert.Equal(5m, usage.Quantity);
        Assert.Equal("eShopOnWeb order 42", usage.Memo);
        Assert.Equal("api-call", usage.ComponentHandle);
        Assert.Equal(60001, usage.SubscriptionId);

        var post = handler.LastRequest;
        Assert.Equal(HttpMethod.Post, post.Method);
        Assert.Contains("\"quantity\":5", post.Body, StringComparison.Ordinal);
        Assert.Contains("eShopOnWeb order 42", post.Body, StringComparison.Ordinal);
        Assert.Contains("/usages", post.Path, StringComparison.Ordinal);

        // The component's numeric id is resolved from the handle and used on the route.
        Assert.Contains("3062731", post.Path, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-25)]
    public async Task RecordUsageAsync_RejectsANonPositiveQuantity_WithoutTouchingTheProvider(decimal quantity)
    {
        var handler = new FakeMaxioHandler();
        var (client, _) = TestClientFactory.Create(handler);

        var exception = await Assert.ThrowsAsync<InvalidUsageQuantityException>(
            () => client.RecordUsageAsync(60001, quantity, "should never be sent"));

        Assert.Equal(quantity, exception.Quantity);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task RecordUsageAsync_RefusesToRecord_WhenTheComponentIsNotMetered()
    {
        var handler = new FakeMaxioHandler()
            .EnqueueOk(MaxioPayloads.ComponentResponse(kind: "quantity_based_component"));

        var (client, _) = TestClientFactory.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => client.RecordUsageAsync(60001, 1m, null));

        Assert.Contains("not metered", exception.Message, StringComparison.Ordinal);

        // Only the component lookup happened; no usage was ever posted.
        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, handler.LastRequest.Method);
    }

    [Fact]
    public async Task RecordUsageAsync_SurfacesTheProvidersValidationMessage()
    {
        var handler = WithComponent(new FakeMaxioHandler())
            .Enqueue(HttpStatusCode.UnprocessableEntity,
                MaxioPayloads.ValidationErrors("Quantity: must be a number."));

        var (client, _) = TestClientFactory.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.RecordUsageAsync(60001, 1m, null));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("Quantity: must be a number.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPeriodToDateUsageAsync_ReturnsTheRunningUnitBalance()
    {
        var handler = WithComponent(new FakeMaxioHandler())
            .EnqueueOk(MaxioPayloads.SubscriptionComponentResponse(unitBalance: 12));

        var (client, _) = TestClientFactory.Create(handler);

        Assert.Equal(12m, await client.GetPeriodToDateUsageAsync(60001));
    }

    [Fact]
    public async Task GetPeriodToDateUsageAsync_SumsTheUsageLog_WhenNoUnitBalanceIsReported()
    {
        var handler = WithComponent(new FakeMaxioHandler())
            .EnqueueOk(MaxioPayloads.SubscriptionComponentResponse(unitBalance: null))
            .EnqueueOk(MaxioPayloads.UsageList((1, 3), (2, 4), (3, 5)));

        var (client, _) = TestClientFactory.Create(handler);

        Assert.Equal(12m, await client.GetPeriodToDateUsageAsync(60001));
    }

    [Fact]
    public async Task GetPeriodToDateUsageAsync_SumsTheUsageLog_WhenTheComponentIsNotYetOnTheSubscription()
    {
        var handler = WithComponent(new FakeMaxioHandler())
            .Enqueue(HttpStatusCode.NotFound, """{"error":"no allocation"}""")
            .EnqueueOk(MaxioPayloads.UsageList((1, 2)));

        var (client, _) = TestClientFactory.Create(handler);

        Assert.Equal(2m, await client.GetPeriodToDateUsageAsync(60001));
    }

    [Fact]
    public async Task GetPeriodToDateUsageAsync_WalksEveryPage_SoALongUsageLogIsNotUndercounted()
    {
        // A full first page must not be mistaken for the whole log.
        var firstPage = Enumerable.Range(1, 100).Select(i => ((long)i, 1)).ToArray();

        var handler = WithComponent(new FakeMaxioHandler())
            .EnqueueOk(MaxioPayloads.SubscriptionComponentResponse(unitBalance: null))
            .EnqueueOk(MaxioPayloads.UsageList(firstPage))
            .EnqueueOk(MaxioPayloads.UsageList((101, 7)));

        var (client, _) = TestClientFactory.Create(handler);

        Assert.Equal(107m, await client.GetPeriodToDateUsageAsync(60001));

        Assert.Contains("page=2", handler.LastRequest.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPeriodToDateUsageAsync_ReturnsZero_WhenNoUsageHasBeenRecordedYet()
    {
        var handler = WithComponent(new FakeMaxioHandler())
            .EnqueueOk(MaxioPayloads.SubscriptionComponentResponse(unitBalance: null))
            .EnqueueOk("[]");

        var (client, _) = TestClientFactory.Create(handler);

        // An empty usage log is a real zero, not an unavailable total.
        Assert.Equal(0m, await client.GetPeriodToDateUsageAsync(60001));
    }

    [Fact]
    public async Task GetPeriodToDateUsageAsync_Throws_WhenTheUsageLogItselfCannotBeRead()
    {
        var handler = WithComponent(new FakeMaxioHandler())
            .EnqueueOk(MaxioPayloads.SubscriptionComponentResponse(unitBalance: null))
            .Enqueue(HttpStatusCode.ServiceUnavailable, """{"error":"upstream down"}""");

        var (client, _) = TestClientFactory.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.GetPeriodToDateUsageAsync(60001));

        Assert.Equal(503, exception.StatusCode);
    }
}
