using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionBillingTests;

public class MaxioSubscriptionBillingServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";
    private const string ProductHandle = "eshop-pro";
    private const string UserName = "demouser@microsoft.com";

    [Fact]
    public async Task ListPlansAsync_ReturnsMappedCatalog()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            return Json(HttpStatusCode.OK, """
                [
                  {
                    "product": {
                      "id": 1,
                      "name": "Pro Plan",
                      "handle": "eshop-pro",
                      "description": "$299/mo",
                      "price_in_cents": 29900,
                      "interval": 1,
                      "interval_unit": "month"
                    }
                  }
                ]
                """);
        });

        var service = CreateService(handler);

        var plans = await service.ListPlansAsync(CancellationToken.None);

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(299.00m, plan.Price);
        Assert.Equal("month", plan.IntervalUnit);
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsExistingSubscription_WhenReferenceAlreadyExists()
    {
        var posts = 0;
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("/customers/lookup"))
            {
                return Json(HttpStatusCode.OK, """
                    { "customer": { "id": 10, "email": "demouser@microsoft.com", "reference": "demouser@microsoft.com", "first_name": "demouser", "last_name": "eShop" } }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.Contains("/subscriptions/lookup"))
            {
                return Json(HttpStatusCode.OK, """
                    {
                      "subscription": {
                        "id": 55,
                        "state": "active",
                        "product_price_in_cents": 29900,
                        "next_assessment_at": "2026-09-20T00:00:00Z",
                        "product": { "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900 }
                      }
                    }
                    """);
            }

            if (request.Method == HttpMethod.Post)
            {
                posts++;
            }

            return Json(HttpStatusCode.NotFound, """{ "errors": "not found" }""");
        });

        var service = CreateService(handler);

        var result = await service.SubscribeAsync(UserName, ProductHandle, CancellationToken.None);

        Assert.Equal(55, result.Id);
        Assert.Equal("eshop-pro", result.ProductHandle);
        Assert.Equal(299.00m, result.Price);
        Assert.Equal("active", result.State);
        Assert.Equal(0, posts);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("/product_families"))
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

            if (request.Method == HttpMethod.Get && path.Contains("/customers/lookup"))
            {
                return Json(HttpStatusCode.NotFound, "{}");
            }

            if (request.Method == HttpMethod.Post && path.Contains("/customers"))
            {
                return Json(HttpStatusCode.Created, """
                    { "customer": { "id": 10, "email": "demouser@microsoft.com", "reference": "demouser@microsoft.com", "first_name": "demouser", "last_name": "eShop" } }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.Contains("/subscriptions/lookup"))
            {
                return Json(HttpStatusCode.NotFound, "{}");
            }

            if (request.Method == HttpMethod.Get && path.Contains("/customers/10/subscriptions"))
            {
                return Json(HttpStatusCode.OK, "[]");
            }

            if (request.Method == HttpMethod.Post && path.Contains("/subscriptions"))
            {
                return Json(HttpStatusCode.Created, """
                    {
                      "subscription": {
                        "id": 55,
                        "state": "active",
                        "product_price_in_cents": 29900,
                        "next_assessment_at": "2026-09-20T00:00:00Z",
                        "product": { "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900 }
                      }
                    }
                    """);
            }

            return Json(HttpStatusCode.NotFound, "{}");
        });

        var service = CreateService(handler);

        var result = await service.SubscribeAsync(UserName, ProductHandle, CancellationToken.None);

        Assert.Equal(55, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal(299.00m, result.Price);
        Assert.NotNull(result.NextBillingAt);
    }

    [Fact]
    public async Task SubscribeAsync_MapsValidationErrorsAs422()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("/customers/lookup"))
            {
                return Json(HttpStatusCode.OK, """
                    { "customer": { "id": 10, "email": "demouser@microsoft.com", "reference": "demouser@microsoft.com", "first_name": "demouser", "last_name": "eShop" } }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.Contains("/subscriptions/lookup"))
            {
                return Json(HttpStatusCode.NotFound, "{}");
            }

            if (request.Method == HttpMethod.Get && path.Contains("/customers/10/subscriptions"))
            {
                return Json(HttpStatusCode.OK, "[]");
            }

            if (request.Method == HttpMethod.Post && path.Contains("/subscriptions"))
            {
                return Json(HttpStatusCode.UnprocessableEntity, """{ "errors": ["product must be specified"] }""");
            }

            return Json(HttpStatusCode.NotFound, "{}");
        });

        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<BillingException>(
            () => service.SubscribeAsync(UserName, ProductHandle, CancellationToken.None));

        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("product must be specified", ex.Message);
    }

    [Fact]
    public async Task GetSubscriptionsForUserAsync_ReturnsEmpty_WhenCustomerIsMissing()
    {
        var handler = new StubHandler(request => Json(HttpStatusCode.NotFound, "{}"));
        var service = CreateService(handler);

        var result = await service.GetSubscriptionsForUserAsync(UserName, CancellationToken.None);

        Assert.Empty(result);
    }

    private static MaxioSubscriptionBillingService CreateService(StubHandler handler)
    {
        var http = new HttpClient(handler);
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" }
        };
        options.Server.Production.Us.Site = "example";
        var client = new MaxioAdvancedBillingClient(http, options);
        var settings = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "example",
            ProductFamilyHandle = FamilyHandle
        });
        var logger = Substitute.For<IAppLogger<MaxioSubscriptionBillingService>>();
        return new MaxioSubscriptionBillingService(client, settings, logger);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }
}
