using System.Net;
using System.Net.Http;
using System.Text;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";
    private static readonly ShopperIdentity DemoShopper = new()
    {
        UserId = "user-1",
        Email = "demouser@microsoft.com",
        FirstName = "Demouser",
        LastName = "eShopOnWeb",
    };

    [Fact]
    public async Task ListPlans_MapsFamilyProducts()
    {
        var handler = new StubHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            var path = Uri.UnescapeDataString(req.RequestUri!.AbsolutePath);
            Assert.Contains($"/product_families/handle:{FamilyHandle}/products", path);
            return Json(HttpStatusCode.OK, """
                [
                  {
                    "product": {
                      "handle": "eshop-pro",
                      "name": "Pro Plan",
                      "description": "Pro",
                      "price_in_cents": 29900,
                      "interval": 1,
                      "interval_unit": "month",
                      "require_credit_card": false,
                      "product_family": { "handle": "eshop-subscribe" }
                    }
                  },
                  {
                    "product": {
                      "handle": "basic-plan",
                      "name": "Basic Plan",
                      "price_in_cents": 2900,
                      "interval": 1,
                      "interval_unit": "month",
                      "require_credit_card": false,
                      "product_family": { "handle": "eshop-subscribe" }
                    }
                  }
                ]
                """);
        });

        var service = CreateService(handler);
        var plans = await service.ListPlansAsync(CancellationToken.None);

        Assert.Equal(2, plans.Count);
        Assert.Contains(plans, p => p.Handle == "eshop-pro" && p.Price == 299.00m && p.IntervalUnit == "month");
        Assert.Contains(plans, p => p.Handle == "basic-plan" && p.Price == 29.00m);
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerAndSubscription_WhenNew()
    {
        var handler = new StubHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Get && path.Contains("/product_families/"))
            {
                return Json(HttpStatusCode.OK, TwoPlansJson);
            }

            if (req.Method == HttpMethod.Get && path.Contains("/customers/lookup"))
            {
                return Json(HttpStatusCode.NotFound, """{ "errors": "Not Found" }""");
            }

            if (req.Method == HttpMethod.Post && path.EndsWith("/customers.json"))
            {
                return Json(HttpStatusCode.Created, """
                    { "customer": { "id": 42, "reference": "user-1", "email": "demouser@microsoft.com", "first_name": "Demouser", "last_name": "eShopOnWeb" } }
                    """);
            }

            if (req.Method == HttpMethod.Get && path.Contains("/subscriptions/lookup"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (req.Method == HttpMethod.Get && path.Contains("/customers/42/subscriptions"))
            {
                return Json(HttpStatusCode.OK, "[]");
            }

            if (req.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
            {
                return Json(HttpStatusCode.Created, ActiveProSubscriptionJson);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);
        var result = await service.SubscribeAsync(DemoShopper, "eshop-pro", CancellationToken.None);

        Assert.Equal(99, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal("eshop-pro", result.ProductHandle);
        Assert.Equal(299.00m, result.Price);
        Assert.NotNull(result.NextBillingAt);
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/customers.json"));
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json"));
    }

    [Fact]
    public async Task Subscribe_IsIdempotent_WhenLiveSubscriptionExists()
    {
        var createCount = 0;
        var handler = new StubHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Get && path.Contains("/product_families/"))
            {
                return Json(HttpStatusCode.OK, TwoPlansJson);
            }

            if (req.Method == HttpMethod.Get && path.Contains("/customers/lookup"))
            {
                return Json(HttpStatusCode.OK, """
                    { "customer": { "id": 42, "reference": "user-1", "email": "demouser@microsoft.com" } }
                    """);
            }

            if (req.Method == HttpMethod.Get && path.Contains("/subscriptions/lookup"))
            {
                return Json(HttpStatusCode.OK, ActiveProSubscriptionJson);
            }

            if (req.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
            {
                createCount++;
                return Json(HttpStatusCode.Created, ActiveProSubscriptionJson);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateService(handler);
        var first = await service.SubscribeAsync(DemoShopper, "eshop-pro", CancellationToken.None);
        var second = await service.SubscribeAsync(DemoShopper, "eshop-pro", CancellationToken.None);

        Assert.Equal(99, first.Id);
        Assert.Equal(99, second.Id);
        Assert.Equal(0, createCount);
    }

    [Fact]
    public async Task ListMySubscriptions_ReturnsEmpty_WhenCustomerMissing()
    {
        var handler = new StubHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/customers/lookup"))
            {
                return Json(HttpStatusCode.NotFound, """{ "errors": "Not Found" }""");
            }

            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        var service = CreateService(handler);
        var result = await service.ListMySubscriptionsAsync("user-1", CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Subscribe_RejectsUnknownPlan()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, TwoPlansJson));
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => service.SubscribeAsync(DemoShopper, "not-a-plan", CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task ListPlans_MapsProviderNotFound_ToBillingFailure()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.NotFound, "\"not found\""));
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => service.ListPlansAsync(CancellationToken.None));

        Assert.Equal(502, ex.StatusCode);
        Assert.DoesNotContain("JsonException", ex.Message, StringComparison.Ordinal);
    }

    private static MaxioSubscriptionBillingService CreateService(StubHandler handler)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" },
            Environment = ServerEnvironment.Us,
        };
        options.Server.Production.Us.Site = "testsite";

        var client = new MaxioAdvancedBillingClient(new HttpClient(handler) { BaseAddress = new Uri("https://testsite.chargify.com") }, options);
        var maxioOptions = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "testsite",
            ProductFamilyHandle = FamilyHandle,
        });
        return new MaxioSubscriptionBillingService(client, maxioOptions, Substitute.For<ILogger<MaxioSubscriptionBillingService>>());
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private const string TwoPlansJson = """
        [
          {
            "product": {
              "handle": "eshop-pro",
              "name": "Pro Plan",
              "price_in_cents": 29900,
              "interval": 1,
              "interval_unit": "month",
              "product_family": { "handle": "eshop-subscribe" }
            }
          },
          {
            "product": {
              "handle": "basic-plan",
              "name": "Basic Plan",
              "price_in_cents": 2900,
              "interval": 1,
              "interval_unit": "month",
              "product_family": { "handle": "eshop-subscribe" }
            }
          }
        ]
        """;

    private const string ActiveProSubscriptionJson = """
        {
          "subscription": {
            "id": 99,
            "state": "active",
            "product_price_in_cents": 29900,
            "current_period_ends_at": "2026-09-21T00:00:00Z",
            "next_assessment_at": "2026-09-21T00:00:00Z",
            "reference": "user-1:eshop-pro",
            "product": {
              "handle": "eshop-pro",
              "name": "Pro Plan",
              "price_in_cents": 29900,
              "interval": 1,
              "interval_unit": "month"
            }
          }
        }
        """;
}

internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public List<HttpRequestMessage> Requests { get; } = new();

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(_responder(request));
    }
}
