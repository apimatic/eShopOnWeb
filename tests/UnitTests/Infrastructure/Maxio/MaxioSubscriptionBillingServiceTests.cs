using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Tests the Maxio integration against the SDK's own HttpClient seam (no network). Bodies are in the
/// wire shape (snake_case) the SDK deserializes. These prove the create path, the idempotent-reuse
/// guard, error translation, and mapping — deterministically and without touching the sandbox.
/// </summary>
public class MaxioSubscriptionBillingServiceTests
{
    private static readonly SubscriberIdentity Subscriber =
        new("user@example.com", "user@example.com", "user", "eShopOnWeb");

    private static MaxioSettings Settings => new()
    {
        ApiKey = "test-key",
        Subdomain = "test",
        ProductFamilyHandle = "eshop-subscribe",
        RequestTimeoutSeconds = 30
    };

    private static (MaxioSubscriptionBillingService Service, StubHttpMessageHandler Handler) BuildService(
        System.Func<HttpRequestMessage, string?, HttpResponseMessage> responder)
    {
        var handler = new StubHttpMessageHandler(responder);
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" }
        });
        var logger = Substitute.For<ILogger<MaxioSubscriptionBillingService>>();
        return (new MaxioSubscriptionBillingService(client, Settings, logger), handler);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Subscribe_CreatesCustomerAndSubscription_WhenNoneExist()
    {
        var (service, handler) = BuildService((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("/customers/lookup.json"))
                return Json(HttpStatusCode.NotFound, "");                       // customer not found
            if (request.Method == HttpMethod.Post && path.EndsWith("/customers.json"))
                return Json(HttpStatusCode.Created, """{"customer":{"id":501,"reference":"user@example.com"}}""");
            if (request.Method == HttpMethod.Get && path.Contains("/subscriptions.json"))
                return Json(HttpStatusCode.OK, "[]");                            // no existing subscriptions
            if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
                return Json(HttpStatusCode.Created,
                    """{"subscription":{"id":9001,"state":"active","product_price_in_cents":2900,"current_period_ends_at":"2026-10-09T00:00:00Z","product":{"handle":"basic-plan","name":"Basic Plan"},"reference":"eshop-user@example.com-basic-plan"}}""");
            return Json(HttpStatusCode.InternalServerError, "unexpected");
        });

        var result = await service.SubscribeAsync(Subscriber, "basic-plan");

        Assert.False(result.AlreadyExisted);
        Assert.Equal(9001, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal("basic-plan", result.Subscription.PlanHandle);
        Assert.Equal("$29.00", result.Subscription.FormattedPrice);
        Assert.Equal(1, handler.CountRequests(HttpMethod.Post, "/customers.json"));      // customer created once
        Assert.Equal(1, handler.CountRequests(HttpMethod.Post, "/subscriptions.json"));  // subscription created once
    }

    [Fact]
    public async Task Subscribe_ReturnsExistingLiveSubscription_WithoutCreating()
    {
        var (service, handler) = BuildService((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("/customers/lookup.json"))
                return Json(HttpStatusCode.OK, """{"customer":{"id":501,"reference":"user@example.com"}}""");
            if (request.Method == HttpMethod.Get && path.Contains("/subscriptions.json"))
                return Json(HttpStatusCode.OK,
                    """[{"subscription":{"id":7777,"state":"active","product_price_in_cents":29900,"product":{"handle":"eshop-pro","name":"Pro Plan"}}}]""");
            return Json(HttpStatusCode.InternalServerError, "should not create");
        });

        var result = await service.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.True(result.AlreadyExisted);
        Assert.Equal(7777, result.Subscription.Id);
        Assert.Equal(0, handler.CountRequests(HttpMethod.Post, "/customers.json"));       // no new customer
        Assert.Equal(0, handler.CountRequests(HttpMethod.Post, "/subscriptions.json"));   // no new subscription
    }

    [Fact]
    public async Task Subscribe_Translates422_ToBillingExceptionWithProviderMessage()
    {
        var (service, _) = BuildService((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("/customers/lookup.json"))
                return Json(HttpStatusCode.OK, """{"customer":{"id":501}}""");
            if (request.Method == HttpMethod.Get && path.Contains("/subscriptions.json"))
                return Json(HttpStatusCode.OK, "[]");
            if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
                return Json(HttpStatusCode.UnprocessableEntity,
                    """{"errors":["Product with API Handle 'nope' does not exist for this site."]}""");
            return Json(HttpStatusCode.InternalServerError, "");
        });

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => service.SubscribeAsync(Subscriber, "nope"));

        Assert.Equal(422, ex.SuggestedStatusCode);
        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public async Task GetPlans_MapsProductsToPlans()
    {
        var (service, _) = BuildService((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("/products.json"))
                return Json(HttpStatusCode.OK,
                    """[{"product":{"id":1,"handle":"basic-plan","name":"Basic Plan","price_in_cents":2900,"interval":1,"interval_unit":"month","require_credit_card":false}},{"product":{"id":2,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false}}]""");
            return Json(HttpStatusCode.InternalServerError, "");
        });

        var plans = await service.GetPlansAsync();

        Assert.Equal(2, plans.Count);
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal("$299.00", pro.FormattedPrice);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.False(pro.RequiresPaymentMethod);
    }

    [Fact]
    public async Task GetMySubscriptions_ReturnsEmpty_WhenCustomerNotFound()
    {
        var (service, handler) = BuildService((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("/customers/lookup.json"))
                return Json(HttpStatusCode.NotFound, "");
            return Json(HttpStatusCode.InternalServerError, "should not be called");
        });

        var subscriptions = await service.GetMySubscriptionsAsync(Subscriber);

        Assert.Empty(subscriptions);
        Assert.Equal(0, handler.CountRequests(HttpMethod.Get, "/subscriptions.json"));
    }
}
