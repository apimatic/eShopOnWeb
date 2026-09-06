using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public class MaxioSubscriptionServiceTests
{
    private static readonly BillingCustomerProfile Shopper =
        new(userIdentifier: "shopper@example.com", email: "shopper@example.com");

    [Fact]
    public async Task ListPlansAsync_ProjectsProductsOntoPlansOrderedByPrice()
    {
        var transport = new StubHttpMessageHandler().RespondWith(HttpStatusCode.OK, MaxioPayloads.Products(
            MaxioPayloads.Product("pro-plan", "Pro Plan", 29900),
            MaxioPayloads.Product("starter-plan", "Starter Plan", 2900)));

        var plans = await MaxioTestHost.CreateService(transport).ListPlansAsync();

        Assert.Collection(plans,
            plan =>
            {
                Assert.Equal("starter-plan", plan.Handle);
                Assert.Equal(29m, plan.Price);
                Assert.Equal(2900, plan.PriceInCents);
                Assert.Equal("month", plan.IntervalUnit);
                Assert.False(plan.PaymentMethodRequired);
            },
            plan =>
            {
                Assert.Equal("pro-plan", plan.Handle);
                Assert.Equal(299m, plan.Price);
            });

        Assert.Contains("product_families/handle:demo-subscriptions/products.json",
            transport.Requests.Single().Uri.ToString());
    }

    [Fact]
    public async Task ListPlansAsync_ExcludesArchivedProducts()
    {
        var transport = new StubHttpMessageHandler().RespondWith(HttpStatusCode.OK, MaxioPayloads.Products(
            MaxioPayloads.Product("pro-plan", "Pro Plan", 29900),
            MaxioPayloads.Product("retired-plan", "Retired Plan", 900, archivedAt: "2026-01-01T00:00:00+00:00")));

        var plans = await MaxioTestHost.CreateService(transport).ListPlansAsync();

        Assert.Equal(new[] { "pro-plan" }, plans.Select(plan => plan.Handle));
    }

    [Fact]
    public async Task ListPlansAsync_CallsMaxioOnceForRepeatedReads()
    {
        var transport = new StubHttpMessageHandler().RespondWith(HttpStatusCode.OK,
            MaxioPayloads.Products(MaxioPayloads.Product("pro-plan", "Pro Plan", 29900)));

        var service = MaxioTestHost.CreateService(transport);
        await service.ListPlansAsync();
        await service.ListPlansAsync();

        Assert.Single(transport.Requests);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscriptionForANewShopper()
    {
        var transport = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Products(MaxioPayloads.Product("pro-plan", "Pro Plan", 29900)))
            .RespondWith(HttpStatusCode.NotFound)
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Customer(98840116, "eshop-shopper@example.com"))
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Subscriptions())
            .RespondWith(HttpStatusCode.Created, MaxioPayloads.Subscription(94211938, "active", "pro-plan"));

        var result = await MaxioTestHost.CreateService(transport)
            .SubscribeAsync(new SubscribeRequest(Shopper, "pro-plan"));

        Assert.True(result.Created);
        Assert.True(result.CustomerCreated);
        Assert.Equal(94211938, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.True(result.Subscription.IsLive);
        Assert.Equal(299m, result.Subscription.Price);
        Assert.Equal(result.Subscription.NextAssessmentAt, result.Subscription.NextBillingAt);

        var signup = transport.Requests.Single(request =>
            request.Method == HttpMethod.Post && request.Uri.AbsolutePath == "/subscriptions.json");
        Assert.Contains("\"product_handle\":\"pro-plan\"", signup.Body);
        Assert.Contains("\"customer_id\":98840116", signup.Body);
        // Without a payment profile the signup only succeeds when it is invoiced rather than charged.
        Assert.Contains("\"payment_collection_method\":\"remittance\"", signup.Body);
        // Duplicate prevention must be requested on every signup.
        Assert.Contains("\"uniqueness_token\"", signup.Body);
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsTheExistingSubscriptionWithoutCreatingASecondOne()
    {
        var transport = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Products(MaxioPayloads.Product("pro-plan", "Pro Plan", 29900)))
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Customer(98840116, "eshop-shopper@example.com"))
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Subscriptions(
                MaxioPayloads.Subscription(94211938, "active", "pro-plan")));

        var result = await MaxioTestHost.CreateService(transport)
            .SubscribeAsync(new SubscribeRequest(Shopper, "pro-plan"));

        Assert.False(result.Created);
        Assert.False(result.CustomerCreated);
        Assert.Equal(94211938, result.Subscription.Id);
        Assert.DoesNotContain(transport.Requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task SubscribeAsync_SignsUpAgainWhenTheEarlierSubscriptionEnded()
    {
        var transport = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Products(MaxioPayloads.Product("pro-plan", "Pro Plan", 29900)))
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Customer(98840116, "eshop-shopper@example.com"))
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Subscriptions(
                MaxioPayloads.Subscription(94211938, "canceled", "pro-plan")))
            .RespondWith(HttpStatusCode.Created, MaxioPayloads.Subscription(94211999, "active", "pro-plan"));

        var result = await MaxioTestHost.CreateService(transport)
            .SubscribeAsync(new SubscribeRequest(Shopper, "pro-plan"));

        Assert.True(result.Created);
        Assert.Equal(94211999, result.Subscription.Id);
    }

    [Fact]
    public async Task SubscribeAsync_IgnoresSubscriptionsToOtherPlans()
    {
        var transport = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Products(
                MaxioPayloads.Product("pro-plan", "Pro Plan", 29900),
                MaxioPayloads.Product("starter-plan", "Starter Plan", 2900)))
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Customer(98840116, "eshop-shopper@example.com"))
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Subscriptions(
                MaxioPayloads.Subscription(94211938, "active", "starter-plan", 2900)))
            .RespondWith(HttpStatusCode.Created, MaxioPayloads.Subscription(94211999, "active", "pro-plan"));

        var result = await MaxioTestHost.CreateService(transport)
            .SubscribeAsync(new SubscribeRequest(Shopper, "pro-plan"));

        Assert.True(result.Created);
        Assert.Equal("pro-plan", result.Subscription.PlanHandle);
    }

    [Fact]
    public async Task SubscribeAsync_RejectsAPlanOutsideTheConfiguredProductFamily()
    {
        var transport = new StubHttpMessageHandler().RespondWith(HttpStatusCode.OK,
            MaxioPayloads.Products(MaxioPayloads.Product("pro-plan", "Pro Plan", 29900)));

        var exception = await Assert.ThrowsAsync<PlanNotFoundException>(() =>
            MaxioTestHost.CreateService(transport).SubscribeAsync(new SubscribeRequest(Shopper, "some-other-product")));

        Assert.Equal("some-other-product", exception.PlanHandle);
        Assert.Equal(MaxioTestHost.ProductFamilyHandle, exception.ProductFamilyHandle);
        // The signup must never reach Maxio: only plans in the configured family are subscribable.
        Assert.DoesNotContain(transport.Requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task SubscribeAsync_AdoptsTheCustomerCreatedByAConcurrentSignup()
    {
        var transport = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Products(MaxioPayloads.Product("pro-plan", "Pro Plan", 29900)))
            .RespondWith(HttpStatusCode.NotFound)
            .RespondWith(HttpStatusCode.UnprocessableEntity, MaxioPayloads.Errors("Reference: must be unique."))
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Customer(98840116, "eshop-shopper@example.com"))
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Subscriptions())
            .RespondWith(HttpStatusCode.Created, MaxioPayloads.Subscription(94211938, "active", "pro-plan"));

        var result = await MaxioTestHost.CreateService(transport)
            .SubscribeAsync(new SubscribeRequest(Shopper, "pro-plan"));

        Assert.True(result.Created);
        Assert.False(result.CustomerCreated);
        Assert.Equal(98840116, result.Subscription.CustomerId);
    }

    [Fact]
    public async Task SubscribeAsync_SurfacesACustomerRejectionThatIsNotARace()
    {
        var transport = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Products(MaxioPayloads.Product("pro-plan", "Pro Plan", 29900)))
            .RespondWith(HttpStatusCode.NotFound)
            .RespondWith(HttpStatusCode.UnprocessableEntity, MaxioPayloads.Errors("Email: is invalid."))
            .RespondWith(HttpStatusCode.NotFound);

        var exception = await Assert.ThrowsAsync<MaxioUnprocessableEntityException>(() =>
            MaxioTestHost.CreateService(transport).SubscribeAsync(new SubscribeRequest(Shopper, "pro-plan")));

        Assert.Contains("Email: is invalid.", exception.Errors);
    }

    [Fact]
    public async Task SubscribeAsync_ResolvesADuplicateSubmissionFromProviderState()
    {
        var transport = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Products(MaxioPayloads.Product("pro-plan", "Pro Plan", 29900)))
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Customer(98840116, "eshop-shopper@example.com"))
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Subscriptions())
            .RespondWith(HttpStatusCode.Conflict, MaxioPayloads.Errors("DuplicatePrevention::DuplicateSubmissionError"))
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Subscriptions(
                MaxioPayloads.Subscription(94211938, "active", "pro-plan")));

        var result = await MaxioTestHost.CreateService(transport)
            .SubscribeAsync(new SubscribeRequest(Shopper, "pro-plan", idempotencyKey: "double-click"));

        Assert.False(result.Created);
        Assert.Equal(94211938, result.Subscription.Id);
    }

    [Fact]
    public async Task SubscribeAsync_ReportsADuplicateSubmissionThatLeftNoSubscription()
    {
        var transport = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Products(MaxioPayloads.Product("pro-plan", "Pro Plan", 29900)))
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Customer(98840116, "eshop-shopper@example.com"))
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Subscriptions())
            .RespondWith(HttpStatusCode.Conflict, MaxioPayloads.Errors("DuplicatePrevention::DuplicateSubmissionError"))
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Subscriptions());

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() =>
            MaxioTestHost.CreateService(transport)
                .SubscribeAsync(new SubscribeRequest(Shopper, "pro-plan", idempotencyKey: "double-click")));

        Assert.Equal(409, exception.StatusCode);
    }

    [Fact]
    public async Task SubscribeAsync_UsesACallerSuppliedIdempotencyKeyAsTheUniquenessToken()
    {
        var transport = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Products(MaxioPayloads.Product("pro-plan", "Pro Plan", 29900)))
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Customer(98840116, "eshop-shopper@example.com"))
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Subscriptions())
            .RespondWith(HttpStatusCode.Created, MaxioPayloads.Subscription(94211938, "active", "pro-plan"));

        await MaxioTestHost.CreateService(transport)
            .SubscribeAsync(new SubscribeRequest(Shopper, "pro-plan", idempotencyKey: "checkout-42"));

        var signup = transport.Requests.Single(request =>
            request.Method == HttpMethod.Post && request.Uri.AbsolutePath == "/subscriptions.json");
        Assert.Contains("\"uniqueness_token\":\"checkout-42\"", signup.Body);
    }

    [Fact]
    public async Task SubscribeAsync_RelaysAPlanThatDemandsAPaymentMethod()
    {
        var transport = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Products(
                MaxioPayloads.Product("pro-plan", "Pro Plan", 29900, requireCreditCard: true)))
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Customer(98840116, "eshop-shopper@example.com"))
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Subscriptions())
            .RespondWith(HttpStatusCode.UnprocessableEntity,
                MaxioPayloads.Errors("No payment method was on file for the $299.00 balance"));

        var exception = await Assert.ThrowsAsync<MaxioUnprocessableEntityException>(() =>
            MaxioTestHost.CreateService(transport).SubscribeAsync(new SubscribeRequest(Shopper, "pro-plan")));

        Assert.Contains("No payment method was on file for the $299.00 balance", exception.Errors);
    }

    [Fact]
    public async Task ListSubscriptionsAsync_ReturnsNothingForAShopperWithNoBillingCustomer()
    {
        var transport = new StubHttpMessageHandler().RespondWith(HttpStatusCode.NotFound);

        var subscriptions = await MaxioTestHost.CreateService(transport)
            .ListSubscriptionsAsync("shopper@example.com");

        Assert.Empty(subscriptions);
        Assert.Single(transport.Requests);
    }

    [Fact]
    public async Task ListSubscriptionsAsync_ReturnsTheShoppersSubscriptionsNewestFirst()
    {
        var transport = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Customer(98840116, "eshop-shopper@example.com"))
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Subscriptions(
                MaxioPayloads.Subscription(94211938, "active", "pro-plan", createdAt: "2026-09-01T10:00:00+00:00"),
                MaxioPayloads.Subscription(94211948, "canceled", "starter-plan", 2900,
                    createdAt: "2026-09-05T10:00:00+00:00")));

        var subscriptions = await MaxioTestHost.CreateService(transport)
            .ListSubscriptionsAsync("shopper@example.com");

        Assert.Equal(new long[] { 94211948, 94211938 }, subscriptions.Select(subscription => subscription.Id));
        Assert.False(subscriptions[0].IsLive);
        Assert.True(subscriptions[1].IsLive);

        // The shopper is found by the reference derived from their account, not by a local mapping.
        Assert.Contains("reference=eshop-shopper%40example.com", transport.Requests[0].Uri.Query);
    }

    [Fact]
    public async Task ListSubscriptionsAsync_LooksTheShopperUpCaseInsensitively()
    {
        var transport = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Customer(98840116, "eshop-shopper@example.com"))
            .RespondWith(HttpStatusCode.OK, MaxioPayloads.Subscriptions());

        await MaxioTestHost.CreateService(transport).ListSubscriptionsAsync("Shopper@Example.com");

        Assert.Contains("reference=eshop-shopper%40example.com", transport.Requests[0].Uri.Query);
    }

    [Fact]
    public async Task SubscribeAsync_ReportsAMisconfiguredProductFamily()
    {
        var transport = new StubHttpMessageHandler().RespondWith(HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<BillingConfigurationException>(() =>
            MaxioTestHost.CreateService(transport, "not-a-family")
                .SubscribeAsync(new SubscribeRequest(Shopper, "pro-plan")));
    }

    [Fact]
    public async Task ListPlansAsync_ReportsRejectedCredentials()
    {
        var transport = new StubHttpMessageHandler().RespondWith(HttpStatusCode.Unauthorized, "HTTP Basic: Access denied.");

        await Assert.ThrowsAsync<BillingConfigurationException>(() =>
            MaxioTestHost.CreateService(transport).ListPlansAsync());
    }
}
