using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public class MaxioSubscriptionBillingServiceTests
{
    private static Subscriber Subscriber => new(MaxioTestContext.SubscriberEmail);

    [Fact]
    public async Task GetPlansAsync_ProjectsPricesFromCentsAndCurrencyFromTheSite()
    {
        var handler = new StubHttpMessageHandler().WithCatalog();
        var service = MaxioTestContext.BuildService(handler);

        var plans = await service.GetPlansAsync();

        Assert.Equal(2, plans.Count);
        var pro = plans.Single(plan => plan.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(299m, pro.Price);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal("USD", pro.Currency);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.False(pro.HasTrial);
        Assert.False(pro.RequiresPaymentProfileAtSignup);
        Assert.Equal(29m, plans.Single(plan => plan.Handle == "basic-plan").Price);
    }

    [Fact]
    public async Task GetPlansAsync_ResolvesTheProductFamilyByHandleNotByAConfiguredId()
    {
        // Two families come back; only the one whose handle matches may be read. Numeric ids are not
        // stable across catalog re-seeds, which is why nothing in the integration configures one.
        var handler = new StubHttpMessageHandler().WithCatalog(familyId: 4242);
        var service = MaxioTestContext.BuildService(handler);

        await service.GetPlansAsync();

        Assert.Equal(1, handler.CountOf(HttpMethod.Get, "/product_families/4242/products.json"));
        Assert.Equal(0, handler.CountOf(HttpMethod.Get, "/product_families/999/products.json"));
    }

    [Fact]
    public async Task SubscribeAsync_CreatesTheCustomerWithAStableReferenceAndNoPaymentDetails()
    {
        var handler = new StubHttpMessageHandler()
            .WithCatalog()
            .OnSequence(HttpMethod.Get, "/customers/lookup.json",
                (HttpStatusCode.NotFound, """{"error":"Customer not found"}"""),
                (HttpStatusCode.OK, MaxioTestContext.CustomerJson()))
            .On(HttpMethod.Post, "/customers.json", HttpStatusCode.Created, MaxioTestContext.CustomerJson())
            .On(HttpMethod.Get, "/customers/555/subscriptions.json", HttpStatusCode.OK, "[]")
            .On(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created,
                MaxioTestContext.SubscriptionJson());

        var service = MaxioTestContext.BuildService(handler);

        var result = await service.SubscribeAsync(Subscriber, MaxioTestContext.ProPlanHandle);

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(94212077, result.Subscription.Id);

        var customerPost = handler.LastOf(HttpMethod.Post, "/customers.json");
        Assert.NotNull(customerPost);
        Assert.Contains("\"reference\":\"eshoponweb-demouser@microsoft.com\"", customerPost!.Body);
        Assert.Contains("\"email\":\"demouser@microsoft.com\"", customerPost.Body);
        Assert.Contains("\"first_name\"", customerPost.Body);
        Assert.Contains("\"last_name\"", customerPost.Body);
    }

    [Fact]
    public async Task SubscribeAsync_SendsProductHandleAndCustomerIdAndAnInvoicedCollectionMethod()
    {
        var handler = new StubHttpMessageHandler()
            .WithCatalog(relationshipInvoicing: true)
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioTestContext.CustomerJson())
            .On(HttpMethod.Get, "/customers/555/subscriptions.json", HttpStatusCode.OK, "[]")
            .On(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created,
                MaxioTestContext.SubscriptionJson());

        var service = MaxioTestContext.BuildService(handler);

        await service.SubscribeAsync(Subscriber, MaxioTestContext.ProPlanHandle);

        var post = handler.LastOf(HttpMethod.Post, "/subscriptions.json");
        Assert.NotNull(post);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", post!.Body);
        Assert.Contains("\"customer_id\":555", post.Body);
        // Relationship Invoicing site: remittance is the collection method that needs no card on file.
        Assert.Contains("\"payment_collection_method\":\"remittance\"", post.Body);
        // Nothing that would capture or demand a payment instrument.
        Assert.DoesNotContain("credit_card_attributes", post.Body);
        Assert.DoesNotContain("bank_account_attributes", post.Body);
        Assert.DoesNotContain("payment_profile", post.Body);
        Assert.DoesNotContain("customer_attributes", post.Body);
    }

    [Fact]
    public async Task SubscribeAsync_UsesInvoiceCollectionOnALegacyStatementsSite()
    {
        var handler = new StubHttpMessageHandler()
            .WithCatalog(relationshipInvoicing: false)
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioTestContext.CustomerJson())
            .On(HttpMethod.Get, "/customers/555/subscriptions.json", HttpStatusCode.OK, "[]")
            .On(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created,
                MaxioTestContext.SubscriptionJson());

        var service = MaxioTestContext.BuildService(handler);

        await service.SubscribeAsync(Subscriber, MaxioTestContext.ProPlanHandle);

        Assert.Contains("\"payment_collection_method\":\"invoice\"",
            handler.LastOf(HttpMethod.Post, "/subscriptions.json")!.Body);
    }

    [Fact]
    public async Task SubscribeAsync_WhenTheCustomerAlreadyHasALiveSubscription_CreatesNothing()
    {
        var handler = new StubHttpMessageHandler()
            .WithCatalog()
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioTestContext.CustomerJson())
            .On(HttpMethod.Get, "/customers/555/subscriptions.json", HttpStatusCode.OK,
                MaxioTestContext.SubscriptionListJson(MaxioTestContext.SubscriptionJson()));

        var service = MaxioTestContext.BuildService(handler);

        var result = await service.SubscribeAsync(Subscriber, MaxioTestContext.ProPlanHandle);

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(94212077, result.Subscription.Id);
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public async Task SubscribeAsync_WhenTheOnlySubscriptionIsTerminated_EnrollsAgain()
    {
        var handler = new StubHttpMessageHandler()
            .WithCatalog()
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioTestContext.CustomerJson())
            .On(HttpMethod.Get, "/customers/555/subscriptions.json", HttpStatusCode.OK,
                MaxioTestContext.SubscriptionListJson(
                    MaxioTestContext.SubscriptionJson(id: 1, state: "canceled")))
            .On(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created,
                MaxioTestContext.SubscriptionJson(id: 2));

        var service = MaxioTestContext.BuildService(handler);

        var result = await service.SubscribeAsync(Subscriber, MaxioTestContext.ProPlanHandle);

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(2, result.Subscription.Id);
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_WhenAnUnknownStateIsReturned_TreatsItAsLiveAndDoesNotEnrollTwice()
    {
        // A state this SDK version does not model must not be read as "no longer subscribed".
        var handler = new StubHttpMessageHandler()
            .WithCatalog()
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioTestContext.CustomerJson())
            .On(HttpMethod.Get, "/customers/555/subscriptions.json", HttpStatusCode.OK,
                MaxioTestContext.SubscriptionListJson(
                    MaxioTestContext.SubscriptionJson(state: "some_future_state")));

        var service = MaxioTestContext.BuildService(handler);

        var result = await service.SubscribeAsync(Subscriber, MaxioTestContext.ProPlanHandle);

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_WhenTheConnectionDropsMidWrite_TheSubscriptionPostIsNeverResent()
    {
        // The SDK retries a transport failure on every verb, and that cannot be switched off. Without the
        // single-send guard this enrolls the customer twice.
        var handler = new StubHttpMessageHandler()
            .WithCatalog()
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioTestContext.CustomerJson())
            .On(HttpMethod.Get, "/customers/555/subscriptions.json", HttpStatusCode.OK, "[]")
            .OnThrows(HttpMethod.Post, "/subscriptions.json", new HttpRequestException("connection reset"));

        var service = MaxioTestContext.BuildService(handler);

        await Assert.ThrowsAsync<BillingProviderException>(
            () => service.SubscribeAsync(Subscriber, MaxioTestContext.ProPlanHandle));

        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_WhenTheWriteOutcomeIsUnknown_ItIsSettledByRereadingProviderState()
    {
        // The POST reached Maxio and was applied; only the answer was lost. Re-reading finds the
        // subscription, so the caller sees the success that actually happened.
        var handler = new StubHttpMessageHandler()
            .WithCatalog()
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioTestContext.CustomerJson())
            .OnSequence(HttpMethod.Get, "/customers/555/subscriptions.json",
                (HttpStatusCode.OK, "[]"),
                (HttpStatusCode.OK, MaxioTestContext.SubscriptionListJson(MaxioTestContext.SubscriptionJson())))
            .OnThrows(HttpMethod.Post, "/subscriptions.json", new HttpRequestException("connection reset"));

        var service = MaxioTestContext.BuildService(handler);

        var result = await service.SubscribeAsync(Subscriber, MaxioTestContext.ProPlanHandle);

        Assert.Equal(94212077, result.Subscription.Id);
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_WhenMaxioRejectsTheSubscription_SurfacesAClientErrorNotAnOutage()
    {
        var handler = new StubHttpMessageHandler()
            .WithCatalog()
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioTestContext.CustomerJson())
            .On(HttpMethod.Get, "/customers/555/subscriptions.json", HttpStatusCode.OK, "[]")
            .On(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.UnprocessableEntity,
                """{"errors":["No payment method was on file for the $299.00 balance"]}""");

        var service = MaxioTestContext.BuildService(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => service.SubscribeAsync(Subscriber, MaxioTestContext.ProPlanHandle));

        Assert.Equal(BillingFailureKind.Rejected, exception.Kind);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, exception.ProviderStatusCode);
        // The provider's own wording must not reach the caller.
        Assert.DoesNotContain("299", exception.Message);
    }

    [Fact]
    public async Task SubscribeAsync_WhenMaxioIsDown_ReportsUnavailableRatherThanRejected()
    {
        var handler = new StubHttpMessageHandler()
            .On(HttpMethod.Get, "/site.json", HttpStatusCode.ServiceUnavailable, "upstream down");

        var service = MaxioTestContext.BuildService(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => service.SubscribeAsync(Subscriber, MaxioTestContext.ProPlanHandle));

        Assert.Equal(BillingFailureKind.Unavailable, exception.Kind);
    }

    [Fact]
    public async Task SubscribeAsync_WhenThePlanHandleIsUnknown_ReportsNotFoundAndCallsNothingElse()
    {
        var handler = new StubHttpMessageHandler().WithCatalog();
        var service = MaxioTestContext.BuildService(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => service.SubscribeAsync(Subscriber, "no-such-plan"));

        Assert.Equal(BillingFailureKind.NotFound, exception.Kind);
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/customers.json"));
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_WithNoPlanHandle_UsesTheConfiguredDefaultPlan()
    {
        var handler = new StubHttpMessageHandler()
            .WithCatalog()
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioTestContext.CustomerJson())
            .On(HttpMethod.Get, "/customers/555/subscriptions.json", HttpStatusCode.OK, "[]")
            .On(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created,
                MaxioTestContext.SubscriptionJson());

        var service = MaxioTestContext.BuildService(handler);

        await service.SubscribeAsync(Subscriber, planHandle: null);

        Assert.Contains("\"product_handle\":\"eshop-pro\"",
            handler.LastOf(HttpMethod.Post, "/subscriptions.json")!.Body);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_MapsStatePeriodAndNextBillingDate()
    {
        var handler = new StubHttpMessageHandler()
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioTestContext.CustomerJson())
            .On(HttpMethod.Get, "/customers/555/subscriptions.json", HttpStatusCode.OK,
                MaxioTestContext.SubscriptionListJson(MaxioTestContext.SubscriptionJson()));

        var service = MaxioTestContext.BuildService(handler);

        var subscriptions = await service.GetSubscriptionsAsync(Subscriber);

        var subscription = Assert.Single(subscriptions);
        Assert.Equal(94212077, subscription.Id);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal("active", subscription.State);
        Assert.Equal(299m, subscription.Price);
        Assert.Equal("USD", subscription.Currency);
        Assert.Equal("remittance", subscription.PaymentCollectionMethod);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 16, 11, 53, TimeSpan.Zero),
            subscription.NextBillingAt!.Value.ToUniversalTime());
        Assert.Equal(new DateTimeOffset(2026, 9, 6, 16, 11, 53, TimeSpan.Zero),
            subscription.CurrentPeriodStartedAt!.Value.ToUniversalTime());
    }

    [Fact]
    public async Task GetSubscriptionsAsync_WhenTheUserHasNoBillingCustomer_ReturnsEmpty()
    {
        var handler = new StubHttpMessageHandler()
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.NotFound,
                """{"error":"Customer not found"}""");

        var service = MaxioTestContext.BuildService(handler);

        Assert.Empty(await service.GetSubscriptionsAsync(Subscriber));
    }

    [Fact]
    public async Task GetSubscriptionsAsync_WhenTheLookupResponseIsUnreadable_DoesNotReportNoSubscriptions()
    {
        // "I could not read the answer" is not "this user has no customer". Reporting an empty list here
        // would let a corrupt response cause a duplicate enrollment on the next subscribe.
        var handler = new StubHttpMessageHandler()
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, "{ this is not json");

        var service = MaxioTestContext.BuildService(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => service.GetSubscriptionsAsync(Subscriber));

        Assert.Equal(BillingFailureKind.Unknown, exception.Kind);
    }
}
