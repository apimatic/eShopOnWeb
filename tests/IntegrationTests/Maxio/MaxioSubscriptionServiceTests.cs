using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Maxio;

/// <summary>
/// Exercises <see cref="MaxioSubscriptionService"/> against a stubbed HTTP transport (no network),
/// focusing on the correctness-critical behaviours: the error boundary, "not found" as absence,
/// provider-status mapping, and the idempotent subscribe flow.
/// </summary>
public class MaxioSubscriptionServiceTests
{
    private const string Reference = "demouser@microsoft.com";

    private static readonly SubscriberInfo Subscriber =
        new(Reference, Reference, "Demouser", "eShopOnWeb");

    private static MaxioSubscriptionService BuildService(StubHttpMessageHandler handler)
    {
        var options = new MaxioAdvancedBillingClientOptions { Environment = ServerEnvironment.Us };
        options.Server.Production.Us.Site = "test";
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);

        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test",
            ProductFamilyHandle = "eshop-subscribe",
            DefaultProductHandle = "eshop-pro",
            RequestTimeoutSeconds = 30
        });

        var logger = Substitute.For<IAppLogger<MaxioSubscriptionService>>();
        return new MaxioSubscriptionService(client, settings, new KeyedAsyncLock(), logger);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    // ---- Error boundary --------------------------------------------------------------------

    [Fact]
    public async Task GetPlans_TransportFailure_SurfacesAsBillingException()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("connection reset"));
        var service = BuildService(handler);

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(() => service.GetPlansAsync());

        Assert.Equal(502, ex.ProviderStatusCode);
        Assert.False(ex.IsClientError);
    }

    [Fact]
    public async Task GetPlans_ProviderServerError_MapsStatusAndIsNotClientError()
    {
        var handler = new StubHttpMessageHandler(_ => Json(HttpStatusCode.InternalServerError, "{\"error\":\"boom\"}"));
        var service = BuildService(handler);

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(() => service.GetPlansAsync());

        Assert.Equal(500, ex.ProviderStatusCode);
        Assert.False(ex.IsClientError);
    }

    // ---- "Not found" is absence, not an error ----------------------------------------------

    [Fact]
    public async Task GetSubscriptions_ReturnsEmpty_WhenCustomerNotFound()
    {
        // A 404 on the customer lookup means "no customer yet", not an error.
        var handler = new StubHttpMessageHandler(_ => Json(HttpStatusCode.NotFound, "{\"errors\":[\"not found\"]}"));
        var service = BuildService(handler);

        var result = await service.GetSubscriptionsAsync(Reference);

        Assert.Empty(result);
    }

    // ---- Plans -----------------------------------------------------------------------------

    [Fact]
    public async Task GetPlans_ResolvesFamilyByHandle_AndMapsProducts()
    {
        const string families = "[{\"product_family\":{\"id\":3023074,\"handle\":\"eshop-subscribe\",\"name\":\"eShop Subscribe\"}}," +
                                "{\"product_family\":{\"id\":999,\"handle\":\"other\",\"name\":\"Other\"}}]";
        const string products = "[{\"product\":{\"id\":7126957,\"name\":\"Pro Plan\",\"handle\":\"eshop-pro\"," +
                                "\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\"}}]";

        var handler = new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/products")) return Json(HttpStatusCode.OK, products);
            return Json(HttpStatusCode.OK, families); // list product families
        });
        var service = BuildService(handler);

        var plans = await service.GetPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal("month", plan.IntervalUnit);
        Assert.Equal("$299.00", plan.FormattedPrice);

        // The products call must target the numeric family id resolved from the handle, not the handle.
        var productsRequest = handler.Requests.First(r => r.RequestUri!.AbsolutePath.Contains("/products"));
        Assert.Contains("3023074", productsRequest.RequestUri!.AbsolutePath);
    }

    // ---- Subscribe (hero flow) -------------------------------------------------------------

    [Fact]
    public async Task Subscribe_NewCustomer_CreatesCustomerThenSubscription_AndMapsResult()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var method = request.Method;

            if (method == HttpMethod.Get && path.Contains("subscription"))
                return Json(HttpStatusCode.OK, "[]"); // no existing subscriptions
            if (method == HttpMethod.Get)
                return Json(HttpStatusCode.NotFound, "{\"errors\":[\"not found\"]}"); // customer lookup -> absent
            if (method == HttpMethod.Post && path.Contains("subscription"))
                return Json(HttpStatusCode.Created, SubscriptionJson());
            return Json(HttpStatusCode.Created, CustomerJson()); // create customer
        });
        var service = BuildService(handler);

        var result = await service.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal("eshop-pro", result.Subscription.ProductHandle);
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero), result.Subscription.NextBillingDate);

        // Exactly one customer create and one subscription create.
        Assert.Equal(1, handler.Requests.Count(r => r.Method == HttpMethod.Post && !r.RequestUri!.AbsolutePath.Contains("subscription")));
        Assert.Equal(1, handler.Requests.Count(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("subscription")));
    }

    [Fact]
    public async Task Subscribe_WhenAlreadyActive_ReturnsExisting_AndDoesNotCreate()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var method = request.Method;

            if (method == HttpMethod.Get && path.Contains("subscription"))
                return Json(HttpStatusCode.OK, "[{\"subscription\":" + SubscriptionInner() + "}]"); // already subscribed
            if (method == HttpMethod.Get)
                return Json(HttpStatusCode.OK, CustomerJson()); // customer already exists
            return Json(HttpStatusCode.InternalServerError, "{\"error\":\"should not create\"}");
        });
        var service = BuildService(handler);

        var result = await service.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.True(result.AlreadyExisted);
        Assert.Equal("active", result.Subscription.State);
        // No POST should ever be issued on the idempotent path.
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    // ---- Fixtures --------------------------------------------------------------------------

    private static string CustomerJson() =>
        "{\"customer\":{\"id\":123,\"reference\":\"" + Reference + "\",\"email\":\"" + Reference +
        "\",\"first_name\":\"Demouser\",\"last_name\":\"eShopOnWeb\"}}";

    private static string SubscriptionInner() =>
        "{\"id\":555,\"state\":\"active\",\"current_period_ends_at\":\"2026-10-03T00:00:00Z\"," +
        "\"product\":{\"id\":7126957,\"name\":\"Pro Plan\",\"handle\":\"eshop-pro\",\"price_in_cents\":29900," +
        "\"interval\":1,\"interval_unit\":\"month\"}," +
        "\"customer\":{\"id\":123,\"reference\":\"" + Reference + "\"}}";

    private static string SubscriptionJson() => "{\"subscription\":" + SubscriptionInner() + "}";
}
