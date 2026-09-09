using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

/// <summary>
/// Unit tests for <see cref="MaxioSubscriptionService"/> — exercised at the HttpClient seam, with a
/// stub HttpMessageHandler standing in for Maxio. No SDK internals are mocked; every test drives
/// real wire JSON through the real SDK client.
/// </summary>
public class MaxioSubscriptionServiceTests
{
    private const string ProductFamilyJson =
        """[{"product_family":{"id":42,"name":"eShop Subscribe","handle":"eshop-subscribe"}}]""";

    private const string ProductsJson =
        """[{"product":{"id":7126957,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false}},{"product":{"id":7126958,"name":"Basic Plan","handle":"basic-plan","price_in_cents":2900,"interval":1,"interval_unit":"month","require_credit_card":false}}]""";

    private const string CustomerJson =
        """{"customer":{"id":55,"reference":"eshopweb-user-u1","first_name":"Demo","last_name":"User","email":"demouser@microsoft.com"}}""";

    private const string ProProductJson =
        """{"product":{"id":7126957,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false}}""";

    private const string SubscriptionJson =
        """{"subscription":{"id":900,"state":"active","reference":"eshopweb-sub-u1-eshop-pro","current_period_ends_at":"2026-10-09T00:00:00Z","product_price_in_cents":29900,"customer":{"id":55,"reference":"eshopweb-user-u1"},"product":{"id":7126957,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}}""";

    [Fact]
    public async Task ListPlansMapsProductsOfConfiguredFamily()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/product_families.json"))
            {
                return Json(HttpStatusCode.OK, ProductFamilyJson);
            }
            if (path.Contains("/products.json"))
            {
                return Json(HttpStatusCode.OK, ProductsJson);
            }
            return Json(HttpStatusCode.NotFound, "{}");
        });

        var service = CreateService(handler);
        var plans = await service.ListPlansAsync(CancellationToken.None);

        Assert.Equal(2, plans.Count);
        // Deterministic order: cheapest first.
        Assert.Equal("basic-plan", plans[0].Handle);
        Assert.Equal(29m, plans[0].Price);
        Assert.Equal("eshop-pro", plans[1].Handle);
        Assert.Equal(299m, plans[1].Price);
        Assert.Equal("month", plans[1].IntervalUnit);
    }

    [Fact]
    public async Task SubscribeCreatesCustomerAndSubscriptionWithStableReferences()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/customers/lookup.json"))
            {
                return Json(HttpStatusCode.NotFound, """{"errors":["not found"]}""");
            }
            if (path.StartsWith("/products/") && path.EndsWith(".json"))
            {
                return Json(HttpStatusCode.OK, ProProductJson);
            }
            if (path.EndsWith("/subscriptions/lookup.json"))
            {
                return Json(HttpStatusCode.NotFound, "{}");
            }
            if (path.EndsWith("/customers.json") && request.Method == HttpMethod.Post)
            {
                return Json(HttpStatusCode.OK, CustomerJson);
            }
            if (path.EndsWith("/subscriptions.json") && request.Method == HttpMethod.Post)
            {
                return Json(HttpStatusCode.OK, SubscriptionJson);
            }
            return Json(HttpStatusCode.NotFound, "{}");
        });

        var service = CreateService(handler);
        var result = await service.SubscribeAsync("u1", "demouser@microsoft.com", "eshop-pro", CancellationToken.None);

        Assert.Equal(900, result.SubscriptionId);
        Assert.Equal("eshop-pro", result.PlanHandle);
        Assert.Equal("active", result.State);
        Assert.Equal(299m, result.Price);
        Assert.Equal(new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero), result.NextBillingDate);

        // Customer create carried the deterministic reference.
        var customerCreate = handler.Requests.Single(r => r.Method == "POST" && r.Path.EndsWith("/customers.json"));
        Assert.Contains("\"reference\":\"eshopweb-user-u1\"", customerCreate.Body);

        // Subscription create carried the deterministic per-user-per-plan reference and the
        // card-free first-billing deferral.
        var subscriptionCreate = handler.Requests.Single(r => r.Method == "POST" && r.Path.EndsWith("/subscriptions.json"));
        Assert.Contains("\"reference\":\"eshopweb-sub-u1-eshop-pro\"", subscriptionCreate.Body);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", subscriptionCreate.Body);
        Assert.Contains("\"next_billing_at\":", subscriptionCreate.Body);
    }

    [Fact]
    public async Task SubscribeTwiceCreatesSubscriptionOnlyOnce()
    {
        var subscriptionLookups = 0;
        var subscriptionCreates = 0;
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/customers/lookup.json"))
            {
                return Json(HttpStatusCode.OK, CustomerJson);
            }
            if (path.StartsWith("/products/") && path.EndsWith(".json"))
            {
                return Json(HttpStatusCode.OK, ProProductJson);
            }
            if (path.EndsWith("/subscriptions/lookup.json"))
            {
                subscriptionLookups++;
                return subscriptionLookups == 1
                    ? Json(HttpStatusCode.NotFound, "{}")
                    : Json(HttpStatusCode.OK, SubscriptionJson);
            }
            if (path.EndsWith("/subscriptions.json") && request.Method == HttpMethod.Post)
            {
                subscriptionCreates++;
                return Json(HttpStatusCode.OK, SubscriptionJson);
            }
            return Json(HttpStatusCode.NotFound, "{}");
        });

        var service = CreateService(handler);

        var first = await service.SubscribeAsync("u1", "demouser@microsoft.com", "eshop-pro", CancellationToken.None);
        var second = await service.SubscribeAsync("u1", "demouser@microsoft.com", "eshop-pro", CancellationToken.None);

        Assert.Equal(1, subscriptionCreates);
        Assert.Equal(900, first.SubscriptionId);
        Assert.Equal(900, second.SubscriptionId);
    }

    [Fact]
    public async Task SubscribeLostCreateRaceReconcilesByReference()
    {
        var subscriptionLookups = 0;
        var subscriptionCreates = 0;
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/customers/lookup.json"))
            {
                return Json(HttpStatusCode.OK, CustomerJson);
            }
            if (path.StartsWith("/products/") && path.EndsWith(".json"))
            {
                return Json(HttpStatusCode.OK, ProProductJson);
            }
            if (path.EndsWith("/subscriptions/lookup.json"))
            {
                // First lookup (pre-create): not found. Reconciliation lookup after the 422: exists.
                subscriptionLookups++;
                return subscriptionLookups == 1
                    ? Json(HttpStatusCode.NotFound, "{}")
                    : Json(HttpStatusCode.OK, SubscriptionJson);
            }
            if (path.EndsWith("/subscriptions.json") && request.Method == HttpMethod.Post)
            {
                subscriptionCreates++;
                return Json(HttpStatusCode.UnprocessableEntity,
                    """{"errors":["Reference has already been taken"]}""");
            }
            return Json(HttpStatusCode.NotFound, "{}");
        });

        var service = CreateService(handler);
        var result = await service.SubscribeAsync("u1", "demouser@microsoft.com", "eshop-pro", CancellationToken.None);

        Assert.Equal(1, subscriptionCreates);
        Assert.Equal(900, result.SubscriptionId);
    }

    [Fact]
    public async Task SubscribeUnknownPlanSurfaces422()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/customers/lookup.json"))
            {
                return Json(HttpStatusCode.OK, CustomerJson);
            }
            if (path.StartsWith("/products/") && path.EndsWith(".json"))
            {
                // Unknown plan handle: the product read misses.
                return Json(HttpStatusCode.NotFound, """{"errors":["not found"]}""");
            }
            return Json(HttpStatusCode.NotFound, "{}");
        });

        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(() =>
            service.SubscribeAsync("u1", "demouser@microsoft.com", "no-such-plan", CancellationToken.None));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, ex.StatusCode);
        Assert.Contains("no-such-plan", ex.Message);
    }

    [Fact]
    public async Task MySubscriptionsWithoutCustomerReturnsEmpty()
    {
        var handler = new StubHandler(request =>
            Json(HttpStatusCode.NotFound, """{"errors":["not found"]}"""));

        var service = CreateService(handler);
        var result = await service.ListMySubscriptionsAsync("u1", CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task MySubscriptionsMapsCustomerSubscriptions()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/customers/lookup.json"))
            {
                return Json(HttpStatusCode.OK, CustomerJson);
            }
            if (path.Contains("/customers/55/subscriptions.json"))
            {
                return Json(HttpStatusCode.OK, $"[{SubscriptionJson}]");
            }
            return Json(HttpStatusCode.NotFound, "{}");
        });

        var service = CreateService(handler);
        var result = await service.ListMySubscriptionsAsync("u1", CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(900, result[0].SubscriptionId);
        Assert.Equal("eshop-pro", result[0].PlanHandle);
        Assert.Equal("active", result[0].State);
    }

    [Fact]
    public async Task ProviderTransportFailureSurfaces502()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection reset"));

        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(() =>
            service.ListPlansAsync(CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadGateway, ex.StatusCode);
    }

    private static MaxioSubscriptionService CreateService(StubHandler handler)
    {
        var client = new MaxioAdvancedBillingClient(
            new HttpClient(handler),
            new MaxioAdvancedBillingClientOptions
            {
                Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us,
                Retry = MaxioAdvancedBilling.Core.Configuration.RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Delay = TimeSpan.FromMilliseconds(1)
                }
            });
        var settings = new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "eshop-subscribe"
        };
        var provider = MaxioClientProvider.ForTesting(client, settings);
        return new MaxioSubscriptionService(provider, NullLogger<MaxioSubscriptionService>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    public sealed class StubHandler : HttpMessageHandler
    {
        public sealed record SentRequest(string Method, string Path, string? Body);

        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public List<SentRequest> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            // Snapshot method, path and body at send time — request content is disposed afterwards.
            var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            Requests.Add(new SentRequest(request.Method.Method, request.RequestUri!.AbsolutePath, body));
            return Task.FromResult(_responder(request));
        }
    }
}
