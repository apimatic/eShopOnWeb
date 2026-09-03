using System.Net;
using System.Text;
using Maxio;
using Maxio.Core.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string?> Bodies { get; } = new();

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? null : request.Content.ReadAsStringAsync().Result);
        var response = _responder(request);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }
}

public class MaxioSubscriptionBillingServiceTests
{
    private static MaxioClient ClientFor(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://example.chargify.com/") }, new MaxioClientOptions
        {
            Retry = RetryOptions.Disabled() with { Timeout = TimeSpan.FromSeconds(5) },
            Logging = new LoggingOptions { LoggerFactory = NullLoggerFactory.Instance, LogRequestBody = false }
        });

    private static MaxioSubscriptionBillingService ServiceFor(StubHandler handler)
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "example",
            ProductFamilyHandle = "eshop-subscribe"
        });
        return new MaxioSubscriptionBillingService(ClientFor(handler), options, NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task ListPlans_UsesFamilyHandlePrefixAndMapsPrice()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """
            [
              {
                "product": {
                  "name": "Pro Plan",
                  "handle": "eshop-pro",
                  "description": "Monthly pro",
                  "price_in_cents": 29900,
                  "interval": 1,
                  "interval_unit": "month"
                }
              }
            ]
            """));
        var service = ServiceFor(handler);

        var plans = await service.ListPlansAsync(CancellationToken.None);

        Assert.Contains("handle%3Aeshop-subscribe", handler.Requests[0].RequestUri!.AbsolutePath);
        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(299.00m, plan.Price);
        Assert.Equal("month", plan.IntervalUnit);
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerThenSubscription_AndIsIdempotentOnReplay()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/products/handle/") && request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, """{ "product": { "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" } }""");
            }

            if (path.Contains("/customers/lookup") && request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.NotFound, """{"errors":"Not Found"}""");
            }

            if (path.EndsWith("/customers.json") && request.Method == HttpMethod.Post)
            {
                return Json(HttpStatusCode.Created, """{ "customer": { "id": 42, "reference": "demouser@microsoft.com", "email": "demouser@microsoft.com" } }""");
            }

            if (path.Contains("/subscriptions/lookup") && request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (path.EndsWith("/subscriptions.json") && request.Method == HttpMethod.Post)
            {
                return Json(HttpStatusCode.Created, """
                    {
                      "subscription": {
                        "id": 99,
                        "state": "active",
                        "product_price_in_cents": 29900,
                        "next_assessment_at": "2026-10-03T00:00:00Z",
                        "reference": "demouser@microsoft.com:eshop-pro",
                        "product": { "handle": "eshop-pro", "name": "Pro Plan" }
                      }
                    }
                    """);
            }

            return Json(HttpStatusCode.InternalServerError, "{}");
        });
        var service = ServiceFor(handler);

        var created = await service.SubscribeAsync("demouser@microsoft.com", "demouser@microsoft.com", "eshop-pro", CancellationToken.None);

        Assert.Equal(99, created.Id);
        Assert.False(created.AlreadyExisted);
        Assert.Equal("active", created.State);
        Assert.Equal(299.00m, created.Price);
        Assert.Equal("eshop-pro", created.ProductHandle);
        Assert.Contains(handler.Bodies, body => body != null && body.Contains("\"reference\":\"demouser@microsoft.com\""));
        Assert.Contains(handler.Bodies, body => body != null && body.Contains("\"product_handle\":\"eshop-pro\""));
        Assert.Contains(handler.Bodies, body => body != null && body.Contains("\"payment_collection_method\":\"remittance\""));
        Assert.Equal(1, handler.Requests.Count(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json")));
    }

    [Fact]
    public async Task Subscribe_ReturnsExistingSubscription_WhenLookupHits()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/products/handle/"))
            {
                return Json(HttpStatusCode.OK, """{ "product": { "handle": "eshop-pro", "name": "Pro Plan" } }""");
            }

            if (path.Contains("/customers/lookup"))
            {
                return Json(HttpStatusCode.OK, """{ "customer": { "id": 42, "reference": "demouser@microsoft.com" } }""");
            }

            if (path.Contains("/subscriptions/lookup"))
            {
                return Json(HttpStatusCode.OK, """
                    {
                      "subscription": {
                        "id": 99,
                        "state": "active",
                        "product_price_in_cents": 29900,
                        "reference": "demouser@microsoft.com:eshop-pro",
                        "product": { "handle": "eshop-pro", "name": "Pro Plan" }
                      }
                    }
                    """);
            }

            return Json(HttpStatusCode.InternalServerError, "{}");
        });
        var service = ServiceFor(handler);

        var existing = await service.SubscribeAsync("demouser@microsoft.com", "demouser@microsoft.com", "eshop-pro", CancellationToken.None);

        Assert.True(existing.AlreadyExisted);
        Assert.Equal(99, existing.Id);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json"));
    }

    [Fact]
    public async Task Subscribe_UnknownPlan_IsCallerFaultNotFound()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.NotFound, "\"not found\""));
        var service = ServiceFor(handler);

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(() =>
            service.SubscribeAsync("demouser@microsoft.com", "demouser@microsoft.com", "missing-plan", CancellationToken.None));

        Assert.Equal(404, ex.ProviderStatusCode);
        Assert.True(ex.IsCallerFault);
    }

    [Fact]
    public async Task CreateCustomer_DoesNotRetryPostOnTransportFailure()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/products/handle/"))
            {
                return Json(HttpStatusCode.OK, """{ "product": { "handle": "eshop-pro", "name": "Pro" } }""");
            }

            if (path.Contains("/customers/lookup"))
            {
                return Json(HttpStatusCode.NotFound, "{}");
            }

            if (request.Method == HttpMethod.Post)
            {
                throw new HttpRequestException("connection reset");
            }

            return Json(HttpStatusCode.InternalServerError, "{}");
        });
        var service = ServiceFor(handler);

        await Assert.ThrowsAsync<MaxioBillingException>(() =>
            service.SubscribeAsync("demouser@microsoft.com", "demouser@microsoft.com", "eshop-pro", CancellationToken.None));

        Assert.Equal(1, handler.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task ListMySubscriptions_EmptyWhenCustomerMissing()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.NotFound, "{}"));
        var service = ServiceFor(handler);

        var result = await service.ListMySubscriptionsAsync("nobody@microsoft.com", CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task JsonExceptionOnSuccess_IsNotTreatedAsMissing()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "{}"));
        var service = ServiceFor(handler);

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(() =>
            service.ListPlansAsync(CancellationToken.None));

        Assert.Equal("The provider returned a response that could not be processed.", ex.Message);
        Assert.False(ex.IsCallerFault);
    }
}
