using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Billing;

/// <summary>
/// Tests <see cref="MaxioBillingService"/> against the real Maxio SDK client, faking only the
/// transport (the <see cref="HttpClient"/> seam) with a scripted sandbox. This exercises the SDK's
/// real (de)serialization plus the service's mapping, idempotency and error translation.
/// </summary>
public class MaxioBillingServiceTests
{
    private const string FamiliesJson =
        """[{"product_family":{"id":3023074,"handle":"eshop-subscribe","name":"eShop Subscribe"}}]""";

    private const string ProductsJson =
        """
        [
          {"product":{"id":7130999,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}},
          {"product":{"id":7131000,"handle":"basic-plan","name":"Basic Plan","price_in_cents":2900,"interval":1,"interval_unit":"month"}}
        ]
        """;

    private const string CustomerJson =
        """{"customer":{"id":42,"reference":"demo@microsoft.com","email":"demo@microsoft.com"}}""";

    private const string CreatedSubscriptionJson =
        """
        {"subscription":{"id":999,"state":"active",
          "product":{"id":7130999,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900},
          "product_price_in_cents":29900,
          "current_period_started_at":"2026-07-28T00:00:00Z",
          "current_period_ends_at":"2026-08-28T00:00:00Z"}}
        """;

    private static MaxioBillingService BuildService(StubHttpMessageHandler handler)
    {
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions());
        var settings = Options.Create(new MaxioSettings
        {
            ProductFamilyHandle = "eshop-subscribe",
            DefaultPlanHandle = "eshop-pro",
        });
        return new MaxioBillingService(client, settings, NullLogger<MaxioBillingService>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>Scripts the sandbox routes; <paramref name="customerExists"/> toggles the lookup result.</summary>
    private static StubHttpMessageHandler Sandbox(bool customerExists, string listSubscriptionsJson = "[]")
    {
        return new StubHttpMessageHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath.ToLowerInvariant();
            var isPost = request.Method == HttpMethod.Post;

            if (path.Contains("product_families") && path.Contains("/products"))
                return Json(HttpStatusCode.OK, ProductsJson);
            if (path.Contains("product_families"))
                return Json(HttpStatusCode.OK, FamiliesJson);
            if (path.Contains("subscriptions") && isPost)
                return Json(HttpStatusCode.Created, CreatedSubscriptionJson);
            if (path.Contains("subscriptions"))
                return Json(HttpStatusCode.OK, listSubscriptionsJson);
            if (path.Contains("customers") && isPost)
                return Json(HttpStatusCode.Created, CustomerJson);
            if (path.Contains("customers"))
                return customerExists
                    ? Json(HttpStatusCode.OK, CustomerJson)
                    : Json(HttpStatusCode.NotFound, """{"errors":["Customer not found"]}""");

            return Json(HttpStatusCode.NotFound, "{}");
        });
    }

    [Fact]
    public async Task GetPlansAsync_MapsProductsFromConfiguredFamily()
    {
        var service = BuildService(Sandbox(customerExists: true));

        var plans = await service.GetPlansAsync();

        Assert.Equal(2, plans.Count);
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal(299m, pro.Price);
        Assert.Equal("month", pro.IntervalUnit);
    }

    [Fact]
    public async Task SubscribeAsync_WhenNoCustomer_CreatesCustomerThenRemittanceSubscription()
    {
        var handler = Sandbox(customerExists: false);
        var service = BuildService(handler);

        var result = await service.SubscribeAsync(
            new SubscriberIdentity("demo@microsoft.com", "demo@microsoft.com"), planHandle: null);

        Assert.Equal("active", result.State);
        Assert.Equal("eshop-pro", result.PlanHandle);
        Assert.Equal(299m, result.Price);
        Assert.False(result.AlreadyExisted);

        // A customer was created (the lookup 404'd) ...
        Assert.Contains(handler.Requests, r =>
            r.Method == HttpMethod.Post && r.Uri.AbsolutePath.Contains("customers"));

        // ... and the subscription was created with remittance collection (no card), on the default plan.
        var createSub = handler.Requests.Single(r =>
            r.Method == HttpMethod.Post && r.Uri.AbsolutePath.Contains("subscriptions"));
        Assert.Contains("remittance", createSub.Body);
        Assert.Contains("eshop-pro", createSub.Body);
    }

    [Fact]
    public async Task SubscribeAsync_WhenLiveSubscriptionExists_ReturnsItWithoutCreating()
    {
        var existing =
            """
            [{"subscription":{"id":555,"state":"active",
              "product":{"id":7130999,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900},
              "product_price_in_cents":29900,
              "current_period_ends_at":"2026-08-28T00:00:00Z"}}]
            """;
        var handler = Sandbox(customerExists: true, listSubscriptionsJson: existing);
        var service = BuildService(handler);

        var result = await service.SubscribeAsync(
            new SubscriberIdentity("demo@microsoft.com", "demo@microsoft.com"), "eshop-pro");

        Assert.True(result.AlreadyExisted);
        Assert.Equal(555, result.SubscriptionId);
        Assert.DoesNotContain(handler.Requests, r =>
            r.Method == HttpMethod.Post && r.Uri.AbsolutePath.Contains("subscriptions"));
    }

    [Fact]
    public async Task SubscribeAsync_WhenPlanUnknown_ThrowsNotFound()
    {
        var service = BuildService(Sandbox(customerExists: true));

        var ex = await Assert.ThrowsAsync<BillingException>(() =>
            service.SubscribeAsync(new SubscriberIdentity("demo@microsoft.com", "demo@microsoft.com"), "no-such-plan"));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_WhenNoCustomer_ReturnsEmpty()
    {
        var service = BuildService(Sandbox(customerExists: false));

        var subs = await service.GetSubscriptionsAsync("nobody@microsoft.com");

        Assert.Empty(subs);
    }
}
