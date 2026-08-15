using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Maxio;

/// <summary>
/// Exercises <see cref="MaxioBillingService"/> against a stubbed HTTP transport (no network), proving
/// the behaviours that matter for the hero flow: enrollment without a payment method, idempotent reuse,
/// and provider-error translation.
/// </summary>
public class MaxioBillingServiceTests
{
    private static readonly SubscriberIdentity Subscriber =
        new(reference: "demouser@microsoft.com", email: "demouser@microsoft.com", firstName: "Demo", lastName: "User");

    private const string ProPlanHandle = "eshop-pro";

    private static MaxioBillingService BuildService(RecordingHandler handler, string productFamilyHandle = "eshop-subscribe")
    {
        var options = new MaxioAdvancedBillingClientOptions();
        options.Server.Production.Us.BaseUrl = "https://maxio.test";
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);

        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "key",
            Subdomain = "sub",
            ProductFamilyHandle = productFamilyHandle,
            DefaultPlanHandle = ProPlanHandle
        });

        return new MaxioBillingService(client, settings, Substitute.For<IAppLogger<MaxioBillingService>>());
    }

    // Routes a request to a canned response by (method, path keyword) so the stub does not depend on
    // exact wire paths. Subscription checks come before customer checks because the customer-scoped
    // subscriptions list path contains both keywords.
    private static Func<HttpRequestMessage, string, HttpResponseMessage> Router(
        Func<HttpResponseMessage> onReadCustomer,
        Func<HttpResponseMessage> onCreateCustomer,
        Func<HttpResponseMessage> onListSubscriptions,
        Func<HttpResponseMessage> onCreateSubscription)
        => (req, _) =>
        {
            string path = req.RequestUri!.AbsolutePath.ToLowerInvariant();
            bool isSubscription = path.Contains("subscription");
            bool isCustomer = path.Contains("customer");

            if (isSubscription && req.Method == HttpMethod.Get) return onListSubscriptions();
            if (isSubscription && req.Method == HttpMethod.Post) return onCreateSubscription();
            if (isCustomer && req.Method == HttpMethod.Get) return onReadCustomer();
            if (isCustomer && req.Method == HttpMethod.Post) return onCreateCustomer();
            return RecordingHandler.Json(HttpStatusCode.NotFound, "{}");
        };

    private static HttpResponseMessage ActiveSubscription(int id) =>
        RecordingHandler.Json(HttpStatusCode.OK, ActiveSubscriptionBody(id));

    [Fact]
    public async Task Subscribe_WhenNothingExists_CreatesCustomerThenSubscriptionWithoutPayment()
    {
        var handler = new RecordingHandler(Router(
            onReadCustomer: () => RecordingHandler.Json(HttpStatusCode.NotFound, "{}"),
            onCreateCustomer: () => RecordingHandler.Json(HttpStatusCode.OK, """{"customer":{"id":123,"reference":"demouser@microsoft.com"}}"""),
            onListSubscriptions: () => RecordingHandler.Json(HttpStatusCode.OK, "[]"),
            onCreateSubscription: () => ActiveSubscription(555)));
        var service = BuildService(handler);

        CustomerSubscription result = await service.SubscribeAsync(Subscriber, ProPlanHandle, CancellationToken.None);

        Assert.Equal(555, result.SubscriptionId);
        Assert.Equal("active", result.State);
        Assert.Equal(ProPlanHandle, result.PlanHandle);
        Assert.Equal(29900, result.PriceInCents);

        // Exactly one customer create and one subscription create.
        Assert.Single(handler.Requests.Where(r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath.Contains("customer", StringComparison.OrdinalIgnoreCase)));
        var subscriptionPost = handler.Requests.Single(r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath.Contains("subscription", StringComparison.OrdinalIgnoreCase));

        // The subscribe body references the plan by handle and carries no payment method.
        Assert.Contains("product_handle", subscriptionPost.Body);
        Assert.Contains(ProPlanHandle, subscriptionPost.Body);
        Assert.Contains("customer_id", subscriptionPost.Body);
        Assert.DoesNotContain("credit_card", subscriptionPost.Body);
        Assert.DoesNotContain("payment_profile", subscriptionPost.Body);
        Assert.DoesNotContain("bank_account", subscriptionPost.Body);
    }

    [Fact]
    public async Task Subscribe_WhenCustomerAndSubscriptionExist_ReusesAndCreatesNothing()
    {
        var handler = new RecordingHandler(Router(
            onReadCustomer: () => RecordingHandler.Json(HttpStatusCode.OK, """{"customer":{"id":123,"reference":"demouser@microsoft.com"}}"""),
            onCreateCustomer: () => RecordingHandler.Json(HttpStatusCode.InternalServerError, "{}"),
            onListSubscriptions: () => RecordingHandler.Json(HttpStatusCode.OK, $"[{ActiveSubscriptionBody(777)}]"),
            onCreateSubscription: () => RecordingHandler.Json(HttpStatusCode.InternalServerError, "{}")));
        var service = BuildService(handler);

        CustomerSubscription result = await service.SubscribeAsync(Subscriber, ProPlanHandle, CancellationToken.None);

        Assert.Equal(777, result.SubscriptionId);
        // Idempotent: no create calls at all — the double-click never duplicates.
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Subscribe_WhenProviderRejectsSubscription_ThrowsWithProviderStatus()
    {
        var handler = new RecordingHandler(Router(
            onReadCustomer: () => RecordingHandler.Json(HttpStatusCode.NotFound, "{}"),
            onCreateCustomer: () => RecordingHandler.Json(HttpStatusCode.OK, """{"customer":{"id":123}}"""),
            onListSubscriptions: () => RecordingHandler.Json(HttpStatusCode.OK, "[]"),
            onCreateSubscription: () => RecordingHandler.Json(HttpStatusCode.UnprocessableEntity, """{"errors":["Product handle not found"]}""")));
        var service = BuildService(handler);

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(
            () => service.SubscribeAsync(Subscriber, ProPlanHandle, CancellationToken.None));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, ex.StatusCode);
        Assert.Contains("Product handle not found", ex.Message);
    }

    [Fact]
    public async Task GetPlans_MapsProductsInConfiguredFamily()
    {
        var handler = new RecordingHandler((req, _) =>
        {
            string path = req.RequestUri!.AbsolutePath.ToLowerInvariant();
            if (path.Contains("products"))
            {
                return RecordingHandler.Json(HttpStatusCode.OK,
                    """[{"product":{"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false}}]""");
            }

            return RecordingHandler.Json(HttpStatusCode.OK,
                """[{"product_family":{"id":42,"handle":"eshop-subscribe","name":"eShop Subscribe"}}]""");
        });
        var service = BuildService(handler);

        var plans = await service.GetPlansAsync(CancellationToken.None);

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal(1, plan.Interval);
        Assert.Equal("month", plan.IntervalUnit);
        Assert.False(plan.PaymentMethodRequired);
    }

    [Fact]
    public async Task GetMySubscriptions_WhenNoCustomer_ReturnsEmpty()
    {
        var handler = new RecordingHandler(Router(
            onReadCustomer: () => RecordingHandler.Json(HttpStatusCode.NotFound, "{}"),
            onCreateCustomer: () => RecordingHandler.Json(HttpStatusCode.InternalServerError, "{}"),
            onListSubscriptions: () => RecordingHandler.Json(HttpStatusCode.InternalServerError, "{}"),
            onCreateSubscription: () => RecordingHandler.Json(HttpStatusCode.InternalServerError, "{}")));
        var service = BuildService(handler);

        var subscriptions = await service.GetMySubscriptionsAsync(Subscriber, CancellationToken.None);

        Assert.Empty(subscriptions);
    }

    private static string ActiveSubscriptionBody(int id) =>
        "{\"subscription\":{\"id\":" + id + ",\"state\":\"active\",\"product_price_in_cents\":29900," +
        "\"current_period_ends_at\":\"2026-09-15T00:00:00+00:00\"," +
        "\"product\":{\"handle\":\"eshop-pro\",\"name\":\"Pro Plan\"}," +
        "\"customer\":{\"id\":123,\"reference\":\"demouser@microsoft.com\"}}}";
}
