using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioBillingServiceTests
{
    private readonly IAppLogger<MaxioBillingService> _logger = Substitute.For<IAppLogger<MaxioBillingService>>();

    [Fact]
    public async Task ListPlansAsync_MapsProductsFromFamily()
    {
        var json = """
            [
              {
                "product": {
                  "id": 7126957,
                  "name": "Pro Plan",
                  "handle": "eshop-pro",
                  "description": "Pro",
                  "price_in_cents": 29900,
                  "interval": 1,
                  "interval_unit": "month"
                }
              },
              {
                "product": {
                  "id": 7126958,
                  "name": "Basic Plan",
                  "handle": "basic-plan",
                  "description": "Basic",
                  "price_in_cents": 2900,
                  "interval": 1,
                  "interval_unit": "month"
                }
              }
            ]
            """;
        var service = CreateService(_ => Json(HttpStatusCode.OK, json));

        var plans = await service.ListPlansAsync(default);

        Assert.Equal(2, plans.Count);
        Assert.Equal("eshop-pro", plans[0].Handle);
        Assert.Equal(299.00m, plans[0].Price);
        Assert.Equal("basic-plan", plans[1].Handle);
        Assert.Equal(29.00m, plans[1].Price);
    }

    [Fact]
    public async Task ListPlansAsync_ThrowsCallerSafeErrorOnUnreachableProvider()
    {
        var service = CreateService(_ => throw new HttpRequestException("connection reset"));

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => service.ListPlansAsync(default));

        Assert.Equal(503, ex.StatusCode);
        Assert.Equal("The billing provider is unreachable.", ex.Message);
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsExistingSubscriptionWhenReferenceMatches()
    {
        var service = CreateService(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/product_families/") && request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, """
                    [
                      {
                        "product": {
                          "id": 1,
                          "name": "Pro Plan",
                          "handle": "eshop-pro",
                          "price_in_cents": 29900,
                          "interval": 1,
                          "interval_unit": "month"
                        }
                      }
                    ]
                    """);
            }

            if (path.Contains("/customers/lookup") && request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, """
                    { "customer": { "id": 42, "reference": "demouser@microsoft.com", "email": "demouser@microsoft.com", "first_name": "demo", "last_name": "Shopper" } }
                    """);
            }

            if (path.Contains("/subscriptions/lookup") && request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, """
                    {
                      "subscription": {
                        "id": 99,
                        "state": "active",
                        "product_price_in_cents": 29900,
                        "currency": "USD",
                        "reference": "demouser@microsoft.com:eshop-pro",
                        "next_assessment_at": "2026-09-19T00:00:00Z",
                        "product": { "id": 1, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" }
                      }
                    }
                    """);
            }

            return Json(HttpStatusCode.InternalServerError, """{"errors":["unexpected"]}""");
        });

        var result = await service.SubscribeAsync("demouser@microsoft.com", "demouser@microsoft.com", "eshop-pro", default);

        Assert.False(result.Created);
        Assert.Equal(99, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal(299.00m, result.Price);
        Assert.Equal("eshop-pro", result.ProductHandle);
    }

    [Fact]
    public async Task SubscribeAsync_RejectsUnknownPlan()
    {
        var service = CreateService(_ => Json(HttpStatusCode.OK, "[]"));

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => service.SubscribeAsync("demouser@microsoft.com", "demouser@microsoft.com", "not-a-plan", default));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("Unknown subscription plan.", ex.Message);
    }

    private static MaxioBillingService CreateService(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubHandler(responder);
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler)
        {
            BaseAddress = new System.Uri("https://example.chargify.com")
        }, new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials
            {
                Username = "test-key",
                Password = "x"
            }
        });

        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "example",
            ProductFamilyHandle = "eshop-subscribe"
        });

        return new MaxioBillingService(client, options, Substitute.For<IAppLogger<MaxioBillingService>>());
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request));
    }
}
