using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class MaxioSubscriptionBillingServiceTests
{
    [Fact]
    public async Task ListPlansAsync_MapsActiveFamilyProducts()
    {
        var handler = new ScriptedHandler
        {
            Responder = (request, _) =>
            {
                Assert.Contains("product_families/handle:eshop-subscribe/products.json", request.RequestUri!.ToString());
                return Json(HttpStatusCode.OK, """
                [
                  {"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","description":"Pro","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false}},
                  {"product":{"id":2,"name":"Archived","handle":"old","price_in_cents":100,"interval":1,"interval_unit":"month","archived_at":"2020-01-01T00:00:00Z"}}
                ]
                """);
            }
        };

        var service = CreateService(handler);
        var plans = await service.ListPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(299.00m, plan.Price);
        Assert.Equal("month", plan.IntervalUnit);
        Assert.False(plan.RequiresPaymentMethod);
    }

    [Fact]
    public async Task SubscribeAsync_IsIdempotentWhenSubscriptionAlreadyExists()
    {
        var handler = new ScriptedHandler
        {
            Responder = (request, _) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.Contains("/subscriptions/lookup.json"))
                {
                    return Json(HttpStatusCode.OK, """
                    {"subscription":{"id":42,"state":"active","product_price_in_cents":29900,"next_assessment_at":"2026-09-19T00:00:00Z","reference":"buyer-1:eshop-pro","product":{"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900}}}
                    """);
                }

                return Json(HttpStatusCode.InternalServerError, """{"errors":["unexpected"]}""");
            }
        };

        var service = CreateService(handler);
        var result = await service.SubscribeAsync(new SubscribeShopperRequest
        {
            BuyerId = "buyer-1",
            Email = "demouser@microsoft.com",
            UserName = "demouser@microsoft.com",
            ProductHandle = "eshop-pro"
        });

        Assert.Equal(42, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal(299.00m, result.Price);
        Assert.True(result.AlreadyExisted);
        Assert.DoesNotContain(handler.Requests, item => item.Request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerThenSubscription()
    {
        var handler = new ScriptedHandler
        {
            Responder = (request, body) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.Contains("/subscriptions/lookup.json") || path.Contains("/customers/lookup.json"))
                {
                    return Json(HttpStatusCode.NotFound, """{"errors":["Not Found"]}""");
                }

                if (path.Contains("/products.json"))
                {
                    return Json(HttpStatusCode.OK, """
                    [{"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false}}]
                    """);
                }

                if (request.Method == HttpMethod.Post && path.EndsWith("/customers.json"))
                {
                    Assert.Contains("\"reference\":\"buyer-1\"", body);
                    Assert.Contains("uniqueness_token", body);
                    return Json(HttpStatusCode.OK, """
                    {"customer":{"id":7,"email":"demouser@microsoft.com","reference":"buyer-1","first_name":"Demouser","last_name":"Customer"}}
                    """);
                }

                if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
                {
                    Assert.Contains("\"product_handle\":\"eshop-pro\"", body);
                    Assert.Contains("\"customer_id\":7", body);
                    Assert.Contains("\"reference\":\"buyer-1:eshop-pro\"", body);
                    Assert.Contains("\"payment_collection_method\":\"remittance\"", body);
                    return Json(HttpStatusCode.Created, """
                    {"subscription":{"id":99,"state":"active","product_price_in_cents":29900,"next_assessment_at":"2026-09-19T00:00:00Z","current_period_ends_at":"2026-09-19T00:00:00Z","reference":"buyer-1:eshop-pro","product":{"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900}}}
                    """);
                }

                return Json(HttpStatusCode.InternalServerError, """{"errors":["unexpected"]}""");
            }
        };

        var service = CreateService(handler);
        var result = await service.SubscribeAsync(new SubscribeShopperRequest
        {
            BuyerId = "buyer-1",
            Email = "demouser@microsoft.com",
            UserName = "demouser@microsoft.com",
            ProductHandle = "eshop-pro"
        });

        Assert.Equal(99, result.Id);
        Assert.Equal("eshop-pro", result.ProductHandle);
        Assert.False(result.AlreadyExisted);
        Assert.Equal(new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero), result.NextBillingAt);
    }

    [Fact]
    public void SplitName_UsesEmailLocalPart()
    {
        var (first, last) = MaxioSubscriptionBillingService.SplitName("demouser@microsoft.com", "demouser@microsoft.com");
        Assert.Equal("Demouser", first);
        Assert.Equal("Customer", last);
    }

    private static MaxioSubscriptionBillingService CreateService(ScriptedHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "example",
            ProductFamilyHandle = "eshop-subscribe"
        });
        var maxio = new MaxioAdvancedBillingClient(httpClient, options, NullLogger<MaxioAdvancedBillingClient>.Instance);
        return new MaxioSubscriptionBillingService(maxio, NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = new();
        public required Func<HttpRequestMessage, string?, HttpResponseMessage> Responder { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, body));
            return Responder(request, body);
        }
    }
}
