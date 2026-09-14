using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class GetSubscriptionsAsyncTests
{
    private static readonly BillingSubscriber Subscriber =
        new("demouser@microsoft.com", "demouser@microsoft.com");

    [Fact]
    public async Task ReadsTheSubscribersEnrollmentsBackFromTheProvider()
    {
        using var host = MaxioTestHost.Create(new MaxioStubHandler()
            .Route(HttpMethod.Get, "lookup.json", HttpStatusCode.OK, MaxioPayloads.Customer)
            .Route(HttpMethod.Get, "subscriptions.json", HttpStatusCode.OK, MaxioPayloads.ActiveProSubscriptionList));

        var subscriptions = (await host.Service.GetSubscriptionsAsync(Subscriber)).ToList();

        var subscription = Assert.Single(subscriptions);
        Assert.Equal(94211648, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.True(subscription.IsActive);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal(299.00m, subscription.Price);
        Assert.Equal("remittance", subscription.PaymentCollectionMethod);
        Assert.Equal(DateTimeOffset.Parse("2026-10-06T20:22:33-04:00"), subscription.NextBillingAt);
    }

    [Fact]
    public async Task LooksTheSubscriberUpByReferenceRatherThanAnyLocalRecord()
    {
        // Nothing about the subscriber is persisted here, so the only thing the lookup can be keyed on is
        // the reference derived from their login - which is why the list survives a restart.
        using var host = MaxioTestHost.Create(new MaxioStubHandler()
            .Route(HttpMethod.Get, "lookup.json", HttpStatusCode.OK, MaxioPayloads.Customer)
            .Route(HttpMethod.Get, "subscriptions.json", HttpStatusCode.OK, MaxioPayloads.NoSubscriptions));

        await host.Service.GetSubscriptionsAsync(Subscriber);

        var lookup = host.Handler.Requests.First(r => r.Uri.AbsolutePath.Contains("lookup.json"));
        Assert.Contains("reference=eshoponweb-", Uri.UnescapeDataString(lookup.Uri.Query));
    }

    [Fact]
    public async Task ReturnsNothingForASubscriberTheProviderHasNeverSeen()
    {
        using var host = MaxioTestHost.Create(new MaxioStubHandler()
            .Route(HttpMethod.Get, "lookup.json", HttpStatusCode.NotFound, MaxioPayloads.CustomerNotFound));

        Assert.Empty(await host.Service.GetSubscriptionsAsync(Subscriber));
    }
}
