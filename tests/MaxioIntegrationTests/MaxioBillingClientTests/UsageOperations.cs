using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

/// <summary>
/// Pay-as-you-go usage (UC2). Maxio has no server-side "total for this period" call, so the seam
/// sums the usage list from the current period boundary — the arithmetic is the behaviour under test.
/// </summary>
public class UsageOperations
{
    private static MeteredComponent Component(decimal unitPrice = 0.01m) =>
        new(MaxioJson.ComponentId, "api-call", "API Calls", "metered_component", isMetered: true, unitPrice);

    private static Subscription Subscription() =>
        new(MaxioJson.SubscriptionId,
            MaxioJson.UserReference,
            MaxioJson.CustomerId,
            new BillingPlan(MaxioJson.ProPlanId, "eshop-pro", "Pro Plan", 299.00m, 1, "month"),
            SubscriptionState.Active,
            "active")
        {
            CurrentPeriodStartedAt = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(-4))
        };

    [Fact]
    public async Task RecordsUsageAgainstTheSubscriptionsComponent()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Post, "/usages.json", HttpStatusCode.OK, MaxioJson.Usage());

        var recorded = await harness.Client.RecordUsageAsync(
            MaxioJson.SubscriptionId, Component(), 3, "eShopOnWeb order 1");

        Assert.Equal(777, recorded.Id);
        Assert.Equal(3, recorded.Quantity);
        Assert.Equal("eShopOnWeb order 1", recorded.Memo);

        var request = harness.Handler.Requests.Single();
        Assert.Contains($"/subscriptions/{MaxioJson.SubscriptionId}/components/{MaxioJson.ComponentId}/usages.json",
            request.Uri.AbsolutePath);
        Assert.Contains("\"quantity\":3", request.Body.Replace(" ", string.Empty));
    }

    [Fact]
    public async Task SumsThePeriodToDateUsageAcrossRecords()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Get, "/usages.json", HttpStatusCode.OK,
            MaxioJson.UsageList(
                MaxioJson.Usage(id: 1, quantity: "2"),
                MaxioJson.Usage(id: 2, quantity: "5"),
                MaxioJson.Usage(id: 3, quantity: "1")));

        var total = await harness.Client.GetPeriodToDateUsageAsync(Subscription(), Component());

        Assert.Equal(8, total);
    }

    [Fact]
    public async Task ReadsQuantitiesThatMaxioReportsAsStringsAsWellAsNumbers()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Get, "/usages.json", HttpStatusCode.OK,
            MaxioJson.UsageList(
                MaxioJson.Usage(id: 1, quantity: "4"),
                MaxioJson.Usage(id: 2, quantity: "\"6\"")));

        // Maxio types the usage quantity as an int-or-string union; ignoring the string branch
        // would silently under-count the bill.
        var total = await harness.Client.GetPeriodToDateUsageAsync(Subscription(), Component());

        Assert.Equal(10, total);
    }

    [Fact]
    public async Task CountsOnlyUsageFromTheCurrentBillingPeriod()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Get, "/usages.json", HttpStatusCode.OK,
            MaxioJson.UsageList(MaxioJson.Usage(quantity: "2")));

        await harness.Client.GetPeriodToDateUsageAsync(Subscription(), Component());

        // Usage before the period boundary is already on an issued invoice, so the read must be
        // bounded by the period start.
        var query = harness.Handler.Requests.Single().Uri.Query;
        Assert.Contains("since_date", query);
        Assert.Contains("2026-07-01", Uri.UnescapeDataString(query));
    }

    [Fact]
    public async Task ReturnsZeroWhenNoUsageHasBeenRecordedYet()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Get, "/usages.json", HttpStatusCode.OK, MaxioJson.EmptyList);

        var total = await harness.Client.GetPeriodToDateUsageAsync(Subscription(), Component());

        Assert.Equal(0, total);
    }

    [Fact]
    public async Task PagesThroughUsageUntilAShortPageIsReturned()
    {
        using var harness = MaxioTestHarness.Create();

        // A full page of 200 single-unit records, then a short page of 3, then nothing.
        var fullPage = MaxioJson.UsageList(Enumerable.Range(1, 200)
            .Select(i => MaxioJson.Usage(id: i, quantity: "1")).ToArray());
        var shortPage = MaxioJson.UsageList(Enumerable.Range(201, 3)
            .Select(i => MaxioJson.Usage(id: i, quantity: "1")).ToArray());

        harness.Handler.RespondInSequence(HttpMethod.Get, "/usages.json",
            (HttpStatusCode.OK, fullPage),
            (HttpStatusCode.OK, shortPage));

        var total = await harness.Client.GetPeriodToDateUsageAsync(Subscription(), Component());

        // Stopping after the first page would lose 3 billable units.
        Assert.Equal(203, total);
        Assert.Equal(2, harness.Handler.Requests.Count);
    }

    [Fact]
    public async Task SurfacesAProviderRejectionWhenUsageIsRefused()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Post, "/usages.json", HttpStatusCode.UnprocessableEntity,
            MaxioJson.ErrorList("Component is not metered"));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => harness.Client.RecordUsageAsync(MaxioJson.SubscriptionId, Component(), 1, null));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("Component is not metered", exception.ProviderMessages);
    }

    [Fact]
    public async Task DoesNotResendUsageWhenTheProviderCallFails()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Post, "/usages.json", HttpStatusCode.InternalServerError, "boom");

        await Assert.ThrowsAsync<BillingProviderException>(
            () => harness.Client.RecordUsageAsync(MaxioJson.SubscriptionId, Component(), 1, null));

        // Usage is additive: a silent retry would double-bill the same units.
        Assert.Single(harness.Handler.Requests);
    }

    [Fact]
    public async Task SurfacesAProviderFailureWhenTheRunningTotalCannotBeRead()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Get, "/usages.json", HttpStatusCode.Unauthorized, "{}");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => harness.Client.GetPeriodToDateUsageAsync(Subscription(), Component()));

        Assert.Equal(401, exception.StatusCode);
    }
}
