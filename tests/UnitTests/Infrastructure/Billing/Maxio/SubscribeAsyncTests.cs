using System.Net;
using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class SubscribeAsyncTests
{
    private static readonly BillingSubscriber Subscriber =
        new("demouser@microsoft.com", "demouser@microsoft.com");

    private static MaxioStubHandler PlansAndCustomer(HttpStatusCode lookupStatus, string lookupBody) =>
        new MaxioStubHandler()
            .Route(HttpMethod.Get, "products.json", HttpStatusCode.OK, MaxioPayloads.TwoProductsOneArchived)
            .Route(HttpMethod.Get, "lookup.json", lookupStatus, lookupBody)
            .Route(HttpMethod.Post, "customers.json", HttpStatusCode.Created, MaxioPayloads.Customer);

    [Fact]
    public async Task CreatesTheCustomerWhenTheReferenceIsUnknownThenSubscribes()
    {
        using var host = MaxioTestHost.Create(
            PlansAndCustomer(HttpStatusCode.NotFound, MaxioPayloads.CustomerNotFound)
                .Route(HttpMethod.Get, "subscriptions.json", HttpStatusCode.OK, MaxioPayloads.NoSubscriptions)
                .Route(HttpMethod.Post, "subscriptions.json", HttpStatusCode.Created, MaxioPayloads.ActiveProSubscription));

        var result = await host.Service.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(94211648, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal(299.00m, result.Subscription.Price);
        Assert.Equal(DateTimeOffset.Parse("2026-10-06T20:22:33-04:00"), result.Subscription.NextBillingAt);

        var createdCustomer = SingleBody(host, HttpMethod.Post, "customers.json");
        Assert.Equal("demouser@microsoft.com", createdCustomer.GetProperty("customer").GetProperty("email").GetString());
        // Keyed on the reference, which is what Maxio enforces uniqueness on.
        Assert.StartsWith("eshoponweb-",
            createdCustomer.GetProperty("customer").GetProperty("reference").GetString());
    }

    [Fact]
    public async Task SubscribesByPlanHandleAndCustomerIdAndBillsByRemittance()
    {
        using var host = MaxioTestHost.Create(
            PlansAndCustomer(HttpStatusCode.OK, MaxioPayloads.Customer)
                .Route(HttpMethod.Get, "subscriptions.json", HttpStatusCode.OK, MaxioPayloads.NoSubscriptions)
                .Route(HttpMethod.Post, "subscriptions.json", HttpStatusCode.Created, MaxioPayloads.ActiveProSubscription));

        await host.Service.SubscribeAsync(Subscriber, "eshop-pro");

        var body = SingleBody(host, HttpMethod.Post, "subscriptions.json").GetProperty("subscription");

        // Handle, not id: numeric product ids are reassigned when the site is re-seeded.
        Assert.Equal("eshop-pro", body.GetProperty("product_handle").GetString());
        Assert.Equal(60251234, body.GetProperty("customer_id").GetInt32());
        // Without this Maxio tries to settle the first balance and rejects a signup with no card on file.
        Assert.Equal("remittance", body.GetProperty("payment_collection_method").GetString());
        Assert.False(body.TryGetProperty("credit_card_attributes", out _));
    }

    [Fact]
    public async Task ReturnsTheExistingSubscriptionWithoutWritingAgain()
    {
        using var host = MaxioTestHost.Create(
            PlansAndCustomer(HttpStatusCode.OK, MaxioPayloads.Customer)
                .Route(HttpMethod.Get, "subscriptions.json", HttpStatusCode.OK, MaxioPayloads.ActiveProSubscriptionList)
                .Route(HttpMethod.Post, "subscriptions.json", HttpStatusCode.Created, MaxioPayloads.ActiveProSubscription));

        var result = await host.Service.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(94211648, result.Subscription.Id);
        Assert.Empty(Requests(host, HttpMethod.Post, "subscriptions.json"));
    }

    [Fact]
    public async Task SubscribesAgainOnceAPreviousSubscriptionHasEnded()
    {
        using var host = MaxioTestHost.Create(
            PlansAndCustomer(HttpStatusCode.OK, MaxioPayloads.Customer)
                .Route(HttpMethod.Get, "subscriptions.json", HttpStatusCode.OK, MaxioPayloads.CanceledProSubscriptionList)
                .Route(HttpMethod.Post, "subscriptions.json", HttpStatusCode.Created, MaxioPayloads.ActiveProSubscription));

        var result = await host.Service.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.False(result.AlreadySubscribed);
        Assert.Single(Requests(host, HttpMethod.Post, "subscriptions.json"));
    }

    [Fact]
    public async Task ConcurrentSubscribesProduceOneSubscription()
    {
        var subscriptionsSeen = 0;
        var handler = PlansAndCustomer(HttpStatusCode.OK, MaxioPayloads.Customer)
            // First read sees nothing; every read after the write sees the created subscription, the way
            // the provider would behave.
            .RouteFunc(HttpMethod.Get, "subscriptions.json", _ =>
                Volatile.Read(ref subscriptionsSeen) == 0
                    ? MaxioPayloads.NoSubscriptions
                    : MaxioPayloads.ActiveProSubscriptionList)
            .RouteFunc(HttpMethod.Post, "subscriptions.json", _ =>
            {
                Interlocked.Increment(ref subscriptionsSeen);
                return MaxioPayloads.ActiveProSubscription;
            });

        using var host = MaxioTestHost.Create(handler);

        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => host.Service.SubscribeAsync(Subscriber, "eshop-pro")));

        Assert.Single(Requests(host, HttpMethod.Post, "subscriptions.json"));
        Assert.Single(results.Where(r => !r.AlreadySubscribed));
        Assert.All(results, r => Assert.Equal(94211648, r.Subscription.Id));
    }

    [Fact]
    public async Task SendsTheSubscriptionOnlyOnceWhenTheConnectionFails()
    {
        // The SDK retry pipeline re-sends on a transport failure regardless of the HTTP verb, and a reset
        // thrown after the bytes arrived is indistinguishable from one thrown before - so the resend has to
        // be refused rather than relied upon to be harmless.
        using var host = MaxioTestHost.Create(
            PlansAndCustomer(HttpStatusCode.OK, MaxioPayloads.Customer)
                .Route(HttpMethod.Get, "subscriptions.json", HttpStatusCode.OK, MaxioPayloads.NoSubscriptions)
                .Fail(HttpMethod.Post, "subscriptions.json"));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => host.Service.SubscribeAsync(Subscriber, "eshop-pro"));

        Assert.Single(Requests(host, HttpMethod.Post, "subscriptions.json"));
        // The outcome is unknown, not known-failed, so it is reported as retryable rather than as a rejection.
        Assert.Equal(BillingFailure.Unavailable, exception.Failure);
    }

    [Fact]
    public async Task SurfacesAProviderRejectionAsTheCallersToFixWithTheProvidersReason()
    {
        using var host = MaxioTestHost.Create(
            PlansAndCustomer(HttpStatusCode.OK, MaxioPayloads.Customer)
                .Route(HttpMethod.Get, "subscriptions.json", HttpStatusCode.OK, MaxioPayloads.NoSubscriptions)
                .Route(HttpMethod.Post, "subscriptions.json", HttpStatusCode.UnprocessableEntity,
                    MaxioPayloads.NoPaymentMethodError));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => host.Service.SubscribeAsync(Subscriber, "eshop-pro"));

        Assert.Equal(BillingFailure.InvalidRequest, exception.Failure);
        Assert.Equal(422, exception.ProviderStatusCode);
        Assert.Contains("No payment method was on file for the $299.00 balance", exception.ToCallerMessage());
    }

    [Fact]
    public async Task RejectsAPlanThatIsNotOnOfferWithoutTouchingTheCustomer()
    {
        using var host = MaxioTestHost.Create(PlansAndCustomer(HttpStatusCode.OK, MaxioPayloads.Customer));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => host.Service.SubscribeAsync(Subscriber, "not-a-plan"));

        Assert.Equal(BillingFailure.InvalidRequest, exception.Failure);
        Assert.Contains("eshop-pro", exception.ToCallerMessage());
        Assert.Empty(Requests(host, HttpMethod.Post, "customers.json"));
    }

    [Fact]
    public async Task FallsBackToTheConfiguredDefaultPlanWhenTheRequestNamesNone()
    {
        using var host = MaxioTestHost.Create(
            PlansAndCustomer(HttpStatusCode.OK, MaxioPayloads.Customer)
                .Route(HttpMethod.Get, "subscriptions.json", HttpStatusCode.OK, MaxioPayloads.NoSubscriptions)
                .Route(HttpMethod.Post, "subscriptions.json", HttpStatusCode.Created, MaxioPayloads.ActiveProSubscription));

        await host.Service.SubscribeAsync(Subscriber, planHandle: null);

        var body = SingleBody(host, HttpMethod.Post, "subscriptions.json").GetProperty("subscription");
        Assert.Equal("eshop-pro", body.GetProperty("product_handle").GetString());
    }

    private static IReadOnlyList<MaxioStubHandler.RecordedRequest> Requests(MaxioTestHost host,
        HttpMethod method, string pathContains) =>
        host.Handler.Requests
            .Where(r => r.Method == method && r.Uri.AbsolutePath.Contains(pathContains))
            .ToList();

    private static JsonElement SingleBody(MaxioTestHost host, HttpMethod method, string pathContains) =>
        JsonDocument.Parse(Assert.Single(Requests(host, method, pathContains)).Body!).RootElement;
}
