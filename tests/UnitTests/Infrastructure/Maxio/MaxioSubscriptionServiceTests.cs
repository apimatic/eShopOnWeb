using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionServiceTests
{
    private const string UserReference = "demouser@microsoft.com";
    private const string PlanHandle = "eshop-pro";

    private sealed class StubHandler : HttpMessageHandler
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

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static MaxioSubscriptionService CreateService(StubHandler handler)
    {
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions());
        var settings = Options.Create(new MaxioSettings { ProductFamilyHandle = "eshop-subscribe" });
        return new MaxioSubscriptionService(client, settings, NullLogger<MaxioSubscriptionService>.Instance);
    }

    private static string CustomerJson(int id = 123) =>
        $$$"""{"customer": {"id": {{{id}}}, "reference": "{{{UserReference}}}", "email": "{{{UserReference}}}"}}""";

    private static string SubscriptionJson(int id = 42, string handle = PlanHandle, string state = "active") =>
        $$$"""
        {"subscription": {"id": {{{id}}}, "state": "{{{state}}}", "reference": "{{{UserReference}}}:{{{handle}}}",
          "product": {"name": "Pro Plan", "handle": "{{{handle}}}", "price_in_cents": 29900, "interval": 1, "interval_unit": "month"},
          "product_price_in_cents": 29900, "next_assessment_at": "2026-09-26T00:00:00Z"}}
        """;

    [Fact]
    public async Task SubscribeCreatesCustomerWhenMissingThenCreatesSubscription()
    {
        var handler = new StubHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Get && path.Contains("/customers/lookup")) return Json(HttpStatusCode.NotFound, "{}");
            if (req.Method == HttpMethod.Post && path.Contains("/customers")) return Json(HttpStatusCode.Created, CustomerJson());
            if (req.Method == HttpMethod.Get && path.Contains("/customers/123/subscriptions")) return Json(HttpStatusCode.OK, "[]");
            if (req.Method == HttpMethod.Post && path.Contains("/subscriptions")) return Json(HttpStatusCode.Created, SubscriptionJson());
            return Json(HttpStatusCode.NotFound, "{}");
        });
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(UserReference, UserReference, PlanHandle);

        Assert.Equal(42, result.Id);
        Assert.Equal("Pro Plan", result.PlanName);
        Assert.Equal(PlanHandle, result.PlanHandle);
        Assert.Equal("active", result.State);
        Assert.Equal(29900, result.PriceInCents);
        Assert.Equal(new DateTimeOffset(2026, 9, 26, 0, 0, 0, TimeSpan.Zero), result.NextBillingDate);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task SubscribeReturnsExistingActiveSubscriptionWithoutCreating()
    {
        var handler = new StubHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Get && path.Contains("/customers/lookup")) return Json(HttpStatusCode.OK, CustomerJson());
            if (req.Method == HttpMethod.Get && path.Contains("/customers/123/subscriptions"))
                return Json(HttpStatusCode.OK, $"[{SubscriptionJson()}]");
            return Json(HttpStatusCode.NotFound, "{}");
        });
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(UserReference, UserReference, PlanHandle);

        Assert.Equal(42, result.Id);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task SubscribeSurfacesProviderValidationErrors()
    {
        var handler = new StubHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Get && path.Contains("/customers/lookup")) return Json(HttpStatusCode.OK, CustomerJson());
            if (req.Method == HttpMethod.Get && path.Contains("/customers/123/subscriptions")) return Json(HttpStatusCode.OK, "[]");
            if (req.Method == HttpMethod.Post && path.Contains("/subscriptions"))
                return Json(HttpStatusCode.UnprocessableEntity, """{"errors": ["Product: cannot be found"]}""");
            return Json(HttpStatusCode.NotFound, "{}");
        });
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<BillingException>(
            () => service.SubscribeAsync(UserReference, UserReference, "no-such-plan"));

        Assert.Equal((int)HttpStatusCode.UnprocessableEntity, ex.StatusCode);
        Assert.Contains("Product: cannot be found", ex.Message);
    }

    [Fact]
    public async Task ListSubscriptionsReturnsEmptyWhenCustomerDoesNotExist()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.NotFound, "{}"));
        var service = CreateService(handler);

        var result = await service.ListSubscriptionsAsync(UserReference);

        Assert.Empty(result);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ListPlansMatchesFamilyByHandleAndMapsProducts()
    {
        var handler = new StubHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.Contains("/product_families") && !path.Contains("/products"))
                return Json(HttpStatusCode.OK, """[{"product_family": {"id": 3023074, "handle": "eshop-subscribe", "name": "eShop Subscribe"}}]""");
            if (path.Contains("/product_families/3023074/products"))
                return Json(HttpStatusCode.OK, """[{"product": {"name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month"}}]""");
            return Json(HttpStatusCode.NotFound, "{}");
        });
        var service = CreateService(handler);

        var result = await service.ListPlansAsync();

        var plan = Assert.Single(result);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(PlanHandle, plan.Handle);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal("month", plan.IntervalUnit);
    }
}
