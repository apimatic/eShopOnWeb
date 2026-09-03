using System.Net;
using System.Text;
using System.Text.Json;
using Maxio;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    [Fact]
    public void SubscriptionReference_IsDeterministicPerUserAndPlan()
    {
        Assert.Equal("eshop:user-1:eshop-pro",
            MaxioSubscriptionBillingService.BuildSubscriptionReference("user-1", "eshop-pro"));
    }

    [Fact]
    public void SplitDisplayName_UsesEmailLocalPart()
    {
        var (first, last) = MaxioSubscriptionBillingService.SplitDisplayName("demouser@microsoft.com");
        Assert.Equal("demouser", first);
        Assert.Equal("eShopOnWeb", last);
    }

    [Fact]
    public async Task ListPlansAsync_MapsProductEnvelope()
    {
        var json = """
            [
              {
                "product": {
                  "id": 1,
                  "name": "Pro Plan",
                  "handle": "eshop-pro",
                  "description": "Monthly pro",
                  "price_in_cents": 29900,
                  "interval": 1,
                  "interval_unit": "month"
                }
              }
            ]
            """;

        var (service, handler) = CreateService((_, _) => Json(HttpStatusCode.OK, json));

        var plans = await service.ListPlansAsync(CancellationToken.None);

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal(299.00m, plan.Price);
        Assert.Equal("month", plan.IntervalUnit);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Contains("/product_families/handle%3Aeshop-subscribe/products.json", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Contains("per_page=200", handler.LastRequest.RequestUri.Query);
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsExistingSubscription_WithoutCreating()
    {
        var lookupJson = """
            {
              "subscription": {
                "id": 42,
                "state": "active",
                "product_price_in_cents": 29900,
                "current_period_ends_at": "2026-10-01T00:00:00Z",
                "reference": "eshop:user-1:eshop-pro",
                "product": { "handle": "eshop-pro", "name": "Pro Plan" }
              }
            }
            """;

        var (service, handler) = CreateService((_, _) => Json(HttpStatusCode.OK, lookupJson));

        var result = await service.SubscribeAsync("user-1", "demouser@microsoft.com", "eshop-pro", CancellationToken.None);

        Assert.Equal(42, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal("eshop-pro", result.ProductHandle);
        Assert.Equal(299.00m, result.Price);
        Assert.Single(handler.Requests);
        Assert.Contains("/subscriptions/lookup.json", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("reference=eshop%3Auser-1%3Aeshop-pro", handler.LastRequest.RequestUri.Query);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerThenSubscription_WhenLookupsMiss()
    {
        var (service, handler) = CreateService((request, n) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/subscriptions/lookup.json"))
            {
                return Json(HttpStatusCode.NotFound, "{}");
            }

            if (path.Contains("/customers/lookup.json"))
            {
                return Json(HttpStatusCode.NotFound, "{}");
            }

            if (path.EndsWith("/customers.json") && request.Method == HttpMethod.Post)
            {
                return Json(HttpStatusCode.Created, """
                    { "customer": { "id": 99, "reference": "user-1", "email": "demouser@microsoft.com" } }
                    """);
            }

            if (path.EndsWith("/subscriptions.json") && request.Method == HttpMethod.Post)
            {
                return Json(HttpStatusCode.Created, """
                    {
                      "subscription": {
                        "id": 7,
                        "state": "active",
                        "product_price_in_cents": 2900,
                        "current_period_ends_at": "2026-10-01T00:00:00Z",
                        "reference": "eshop:user-1:basic-plan",
                        "product": { "handle": "basic-plan", "name": "Basic Plan" }
                      }
                    }
                    """);
            }

            return Json(HttpStatusCode.InternalServerError, "{}");
        });

        var result = await service.SubscribeAsync("user-1", "demouser@microsoft.com", "basic-plan", CancellationToken.None);

        Assert.Equal(7, result.Id);
        Assert.Equal("basic-plan", result.ProductHandle);
        Assert.Equal(2, handler.Requests.Count(r => r.Method == HttpMethod.Post));
        Assert.Contains("\"product_handle\":\"basic-plan\"", Compact(handler.Bodies.Last(b => b is not null)!));
        Assert.Contains("\"customer_id\":99", Compact(handler.Bodies.Last(b => b is not null)!));
        Assert.Contains("\"reference\":\"eshop:user-1:basic-plan\"", Compact(handler.Bodies.Last(b => b is not null)!));
        Assert.Contains("\"payment_collection_method\":\"remittance\"", Compact(handler.Bodies.Last(b => b is not null)!));
    }

    [Fact]
    public async Task SubscribeAsync_DoesNotResendCreateOnTransportFault()
    {
        var (service, handler) = CreateService((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/subscriptions/lookup.json") || path.Contains("/customers/lookup.json"))
            {
                return Json(HttpStatusCode.NotFound, "{}");
            }

            if (request.Method == HttpMethod.Post)
            {
                throw new HttpRequestException("connection reset");
            }

            return Json(HttpStatusCode.InternalServerError, "{}");
        });

        await Assert.ThrowsAnyAsync<Exception>(() =>
            service.SubscribeAsync("user-1", "demouser@microsoft.com", "eshop-pro", CancellationToken.None));

        Assert.Equal(1, handler.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task ListSubscriptionsForUserAsync_ReturnsEmptyWhenCustomerMissing()
    {
        var (service, _) = CreateService((_, _) => Json(HttpStatusCode.NotFound, "{}"));

        var result = await service.ListSubscriptionsForUserAsync("user-1", CancellationToken.None);

        Assert.Empty(result);
    }

    private static (MaxioSubscriptionBillingService Service, StubHandler Handler) CreateService(
        Func<HttpRequestMessage, int, HttpResponseMessage> responder)
    {
        var handler = new StubHandler(responder);
        var client = new MaxioClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.chargify.com")
        }, new MaxioClientOptions());

        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-not-used",
            Subdomain = "test",
            ProductFamilyHandle = "eshop-subscribe"
        });

        var service = new MaxioSubscriptionBillingService(client, options, NullLogger<MaxioSubscriptionBillingService>.Instance);
        return (service, handler);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static string Compact(string json) =>
        json.Replace(" ", string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
}

public sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, int, HttpResponseMessage> _responder;
    private int _count;

    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string?> Bodies { get; } = new();
    public HttpRequestMessage? LastRequest => Requests.Count == 0 ? null : Requests[^1];

    public StubHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responder) => _responder = responder;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? null : request.Content.ReadAsStringAsync().Result);
        var n = Interlocked.Increment(ref _count);
        var response = _responder(request, n);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }
}
