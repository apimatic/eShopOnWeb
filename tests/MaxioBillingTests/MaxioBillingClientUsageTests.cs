using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioBillingTests;

/// <summary>
/// Pay-as-you-go usage (UC2): the component handle must resolve to a metered component before anything is
/// recorded, the quantity union must be read on both of its branches, and the running total must be read
/// back from the subscription's metered accumulation.
/// </summary>
public class MaxioBillingClientUsageTests
{
    [Fact]
    public async Task RecordUsageAsync_PostsTheQuantityAndMemo()
    {
        using var context = new BillingTestContext().WithComponentLookup();
        context.Handler.Enqueue(MaxioPayloads.Usage(id: 8801, quantity: 5, memo: "order #42"));

        var record = await context.Client.RecordUsageAsync(
            MaxioPayloads.SubscriptionId, "api-call", 5, "order #42");

        var usageRequest = context.Handler.Requests.Last();
        Assert.Equal(HttpMethod.Post, usageRequest.Method);
        Assert.Contains("\"quantity\":5", usageRequest.Body!);
        Assert.Contains("order #42", usageRequest.Body!);

        Assert.Equal(8801, record.Id);
        Assert.Equal(5, record.Quantity);
        Assert.Equal("order #42", record.Memo);
        Assert.Equal(new DateTimeOffset(2026, 7, 23, 9, 30, 0, TimeSpan.FromHours(-4)), record.RecordedAt);
    }

    [Fact]
    public async Task RecordUsageAsync_AddressesTheComponentByItsResolvedNumericId()
    {
        using var context = new BillingTestContext().WithComponentLookup();
        context.Handler.Enqueue(MaxioPayloads.Usage(id: 8802, quantity: 1));

        await context.Client.RecordUsageAsync(MaxioPayloads.SubscriptionId, "api-call", 1, null);

        var usageRequest = context.Handler.Requests.Last();

        // The handle is resolved to an id first; the usage call itself never carries the handle.
        Assert.Contains(MaxioPayloads.ComponentId.ToString(), usageRequest.Path);
        Assert.Contains(MaxioPayloads.SubscriptionId.ToString(), usageRequest.Path);
    }

    [Fact]
    public async Task RecordUsageAsync_ReadsAQuantityReturnedAsAString()
    {
        using var context = new BillingTestContext().WithComponentLookup();
        context.Handler.Enqueue(MaxioPayloads.UsageWithStringQuantity(id: 8803, quantity: "12"));

        var record = await context.Client.RecordUsageAsync(MaxioPayloads.SubscriptionId, "api-call", 12, null);

        // Maxio returns the quantity as either a number or a string; both must read back as 12.
        Assert.Equal(12, record.Quantity);
    }

    [Fact]
    public async Task RecordUsageAsync_RefusesANonMeteredComponent()
    {
        using var context = new BillingTestContext().WithComponentLookup(MaxioPayloads.QuantityBasedComponents);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => context.Client.RecordUsageAsync(MaxioPayloads.SubscriptionId, "api-call", 1, null));

        Assert.Contains("not metered", exception.Message);

        // Nothing was posted: only the two catalog reads happened.
        Assert.Equal(2, context.Handler.Requests.Count);
        Assert.DoesNotContain(context.Handler.Requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task RecordUsageAsync_RefusesAnUnknownComponentHandle()
    {
        using var context = new BillingTestContext().WithComponentLookup(MaxioPayloads.EmptyList);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => context.Client.RecordUsageAsync(MaxioPayloads.SubscriptionId, "api-call", 1, null));

        Assert.Contains("does not exist", exception.Message);
    }

    [Fact]
    public async Task RecordUsageAsync_RejectsABlankComponentHandle_WithoutCallingMaxio()
    {
        using var context = new BillingTestContext();

        await Assert.ThrowsAsync<ArgumentException>(
            () => context.Client.RecordUsageAsync(MaxioPayloads.SubscriptionId, " ", 1, null));

        Assert.Empty(context.Handler.Requests);
    }

    [Fact]
    public async Task RecordUsageAsync_SurfacesProviderRejection()
    {
        using var context = new BillingTestContext().WithComponentLookup();
        context.Handler.Enqueue(MaxioPayloads.ErrorList, HttpStatusCode.UnprocessableEntity);

        await Assert.ThrowsAsync<BillingProviderException>(
            () => context.Client.RecordUsageAsync(MaxioPayloads.SubscriptionId, "api-call", 3, null));
    }

    [Fact]
    public async Task GetPeriodToDateUsageAsync_ReturnsTheMeteredAccumulation()
    {
        using var context = new BillingTestContext().WithComponentLookup();
        context.Handler.Enqueue(MaxioPayloads.SubscriptionComponent(unitBalance: 250));

        var total = await context.Client.GetPeriodToDateUsageAsync(MaxioPayloads.SubscriptionId, "api-call");

        Assert.Equal(250, total);
    }

    [Fact]
    public async Task GetPeriodToDateUsageAsync_ReturnsZero_WhenNothingHasAccrued()
    {
        using var context = new BillingTestContext().WithComponentLookup();
        context.Handler.Enqueue(MaxioPayloads.SubscriptionComponent(unitBalance: 0));

        Assert.Equal(0, await context.Client.GetPeriodToDateUsageAsync(MaxioPayloads.SubscriptionId, "api-call"));
    }

    [Fact]
    public async Task GetPeriodToDateUsageAsync_ReturnsNull_WhenTheComponentHasNeverBeenUsed()
    {
        using var context = new BillingTestContext().WithComponentLookup();
        context.Handler.EnqueueStatus(HttpStatusCode.NotFound);

        Assert.Null(await context.Client.GetPeriodToDateUsageAsync(MaxioPayloads.SubscriptionId, "api-call"));
    }

    [Fact]
    public async Task GetPeriodToDateUsageAsync_SurfacesRealFailures()
    {
        using var context = new BillingTestContext().WithComponentLookup();
        context.Handler.EnqueueStatus(HttpStatusCode.ServiceUnavailable);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => context.Client.GetPeriodToDateUsageAsync(MaxioPayloads.SubscriptionId, "api-call"));

        Assert.Equal(503, exception.StatusCode);
    }

    [Fact]
    public async Task FindComponentByHandleAsync_FallsBackToTheCentsPrice_WhenNoDollarStringIsPublished()
    {
        using var context = new BillingTestContext()
            .WithComponentLookup(MaxioPayloads.MeteredComponentsPricedInCentsOnly);

        var component = await context.Client.FindComponentByHandleAsync("api-call");

        Assert.NotNull(component);
        // 250 cents is $2.50 — the fallback must convert, not pass the cents figure through as dollars.
        Assert.Equal(2.50m, component!.UnitPrice);
    }
}
