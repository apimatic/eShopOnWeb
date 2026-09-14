using System.Net;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

/// <summary>
/// Unit tests for the Maxio billing boundary (MaxioBillingService), exercising the
/// idempotency and error-translation logic against a stubbed HTTP transport — no real
/// Maxio traffic. Live-sandbox end-to-end verification is a separate, manual step.
/// </summary>
public class MaxioBillingServiceTests : MaxioBillingTestBase
{
    [Fact]
    public async Task ListPlans_MapsPlansFromConfiguredFamily()
    {
        var (service, stub) = CreateService();
        stub.OnGet("product_families.json", FamiliesJson);
        stub.OnGet("product_families/302/products", ProductsJson);

        var plans = await service.ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Contains(plans, p => p.Handle == "eshop-pro" && p.PriceInCents == 29900 && p.IntervalUnit == "month");
        Assert.Contains(plans, p => p.Handle == "basic-plan" && p.PriceInCents == 2900);
        // Ordered by price ascending
        Assert.True(plans[0].PriceInCents <= plans[1].PriceInCents);
    }

    [Fact]
    public async Task Subscribe_NewUser_CreatesCustomerAndSubscription()
    {
        var (service, stub) = CreateService();
        stub.OnGet("subscriptions/lookup", "", HttpStatusCode.NotFound);        // no existing subscription
        stub.OnGet("products/handle/eshop-pro", ProPlanJson);                        // plan is subscribable
        QueueLookups(stub, (HttpStatusCode.NotFound, ""));                    // no customer yet
        stub.OnPost("customers.json", CustomerJson);                          // create customer
        stub.OnPost("subscriptions.json", SubscriptionJson);                  // enroll

        var result = await service.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.True(result.CreatedNew);
        Assert.Equal(456, result.Subscription.SubscriptionId);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal(29900, result.Subscription.PriceInCents);
        Assert.Equal("active", result.Subscription.State);
        Assert.NotNull(result.Subscription.NextBillingAt);

        var customerBody = Assert.Single(PostedBodies(stub, "customers"));
        Assert.Contains("\"reference\":\"eshop-user-" + UserId + "\"", customerBody);
        Assert.Contains("demouser@microsoft.com", customerBody);

        var subscriptionBody = Assert.Single(PostedBodies(stub, "subscriptions"));
        Assert.Contains("\"product_handle\":\"eshop-pro\"", subscriptionBody);
        Assert.Contains("\"customer_id\":123", subscriptionBody);
        Assert.Contains("\"reference\":\"eshop-sub-" + UserId + ":eshop-pro\"", subscriptionBody);
        // No payment information is captured at enrollment
        Assert.DoesNotContain("credit_card", subscriptionBody);
        Assert.DoesNotContain("payment_profile", subscriptionBody);
    }

    [Fact]
    public async Task Subscribe_ExistingSubscription_ReplaysWithoutCreating()
    {
        var (service, stub) = CreateService();
        stub.OnGet("subscriptions/lookup", SubscriptionJson);                   // already subscribed

        var result = await service.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.False(result.CreatedNew);
        Assert.Equal(456, result.Subscription.SubscriptionId);
        Assert.DoesNotContain(stub.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Subscribe_CustomerCreateRejected422_RecoversByLookup()
    {
        var (service, stub) = CreateService();
        stub.OnGet("subscriptions/lookup", "", HttpStatusCode.NotFound);
        stub.OnGet("products/handle/eshop-pro", ProPlanJson);
        QueueLookups(stub,
            (HttpStatusCode.NotFound, ""),                                    // first lookup: no customer
            (HttpStatusCode.OK, CustomerJson));                               // re-lookup after 422: exists
        stub.OnPost("customers.json",
            """{"errors":{"email":["has already been taken"]}}""", HttpStatusCode.UnprocessableEntity);
        stub.OnPost("subscriptions.json", SubscriptionJson);

        var result = await service.SubscribeAsync(Subscriber, "eshop-pro");

        // The provider enforces one customer per reference; a duplicate rejection must
        // resolve to the existing customer and proceed, not fail.
        Assert.True(result.CreatedNew);
        Assert.Equal(1, stub.Requests.Count(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("subscriptions")));
    }

    [Fact]
    public async Task Subscribe_UnknownPlan_ThrowsNotFound()
    {
        var (service, stub) = CreateService();
        stub.OnGet("subscriptions/lookup", "", HttpStatusCode.NotFound);
        stub.OnGet("products/handle/eshop-pro", "", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(() => service.SubscribeAsync(Subscriber, "eshop-pro"));

        Assert.Equal(MaxioBillingErrorKind.NotFound, ex.Kind);
    }

    [Fact]
    public async Task Subscribe_PlanRequiringCreditCard_ThrowsInvalidRequest()
    {
        var (service, stub) = CreateService();
        stub.OnGet("subscriptions/lookup", "", HttpStatusCode.NotFound);
        stub.OnGet("products/handle/eshop-pro",
            """{"product":{"id":712,"handle":"eshop-pro","name":"Pro","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":true}}""");

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(() => service.SubscribeAsync(Subscriber, "eshop-pro"));

        Assert.Equal(MaxioBillingErrorKind.InvalidRequest, ex.Kind);
        Assert.Contains("payment method", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(stub.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Subscribe_Provider422OnCreate_ThrowsInvalidRequestWithProviderMessage()
    {
        var (service, stub) = CreateService();
        stub.OnGet("subscriptions/lookup", "", HttpStatusCode.NotFound);
        stub.OnGet("products/handle/eshop-pro", ProPlanJson);
        stub.OnGet("customers/lookup", CustomerJson);                          // customer already exists
        stub.OnPost("subscriptions.json",
            """{"errors":["Product handle is invalid"]}""", HttpStatusCode.UnprocessableEntity);

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(() => service.SubscribeAsync(Subscriber, "eshop-pro"));

        Assert.Equal(MaxioBillingErrorKind.InvalidRequest, ex.Kind);
        Assert.Contains("Product handle is invalid", ex.Message);
    }

    [Fact]
    public async Task GetSubscriptionsForUser_NoCustomer_ReturnsEmpty()
    {
        var (service, stub) = CreateService();
        stub.OnGet("customers/lookup", "", HttpStatusCode.NotFound);

        var subscriptions = await service.GetSubscriptionsForUserAsync(Subscriber);

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task GetSubscriptionsForUser_WithCustomer_ReturnsMappedSubscriptions()
    {
        var (service, stub) = CreateService();
        stub.OnGet("customers/lookup", CustomerJson);
        stub.OnGet("customers/123/subscriptions",
            """[{"subscription":{"id":456,"state":"active","product_price_in_cents":29900,"product":{"id":712,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}}}]""");

        var subscriptions = await service.GetSubscriptionsForUserAsync(Subscriber);

        var sub = Assert.Single(subscriptions);
        Assert.Equal(456, sub.SubscriptionId);
        Assert.Equal("eshop-pro", sub.PlanHandle);
        Assert.Equal("active", sub.State);
    }
}
