using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioBillingServiceTests
{
    private const string UserReference = "demouser@microsoft.com";
    private const string FamilyHandle = "eshop-subscribe";

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

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static MaxioBillingService CreateService(StubHandler handler)
    {
        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" }
        };
        clientOptions.Server.Production.Us.Site = "test-site";
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), clientOptions);

        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = FamilyHandle
        });

        return new MaxioBillingService(client, options, Substitute.For<IAppLogger<MaxioBillingService>>());
    }

    private const string PlansJson = """
        [
          { "product": { "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900, "interval": 1, "interval_unit": "month", "product_family": { "handle": "eshop-subscribe" } } },
          { "product": { "handle": "basic-plan", "name": "Basic Plan", "price_in_cents": 2900, "interval": 1, "interval_unit": "month", "product_family": { "handle": "eshop-subscribe" } } },
          { "product": { "handle": "old-plan", "name": "Old Plan", "price_in_cents": 100, "interval": 1, "interval_unit": "month", "archived_at": "2026-01-01T00:00:00Z", "product_family": { "handle": "eshop-subscribe" } } }
        ]
        """;

    private const string CustomerJson = """
        { "customer": { "id": 123, "reference": "demouser@microsoft.com", "email": "demouser@microsoft.com", "first_name": "demouser", "last_name": "Customer" } }
        """;

    private const string ActiveSubscriptionJson = """
        { "subscription": { "id": 55, "state": "active", "current_period_ends_at": "2026-09-26T00:00:00Z", "product_price_in_cents": 29900, "product": { "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" } } }
        """;

    [Fact]
    public async Task ListPlansAsync_MapsPlansAndSkipsArchived()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, PlansJson));
        var service = CreateService(handler);

        var plans = await service.ListPlansAsync();

        Assert.Equal(2, plans.Count);
        var pro = Assert.Single(plans, p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.DoesNotContain(plans, p => p.Handle == "old-plan");
        Assert.Contains("eshop-subscribe", handler.Requests[0].RequestUri!.OriginalString);
    }

    [Fact]
    public async Task SubscribeAsync_WhenCustomerMissing_CreatesCustomerThenSubscription()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/customers/lookup.json")) return Json(HttpStatusCode.NotFound, "");
            if (path.Contains("/customers.json") && request.Method == HttpMethod.Post) return Json(HttpStatusCode.Created, CustomerJson);
            if (path.Contains("/customers/123/subscriptions.json")) return Json(HttpStatusCode.OK, "[]");
            if (path.Contains("/product_families/")) return Json(HttpStatusCode.OK, PlansJson);
            if (path.Contains("/subscriptions.json") && request.Method == HttpMethod.Post) return Json(HttpStatusCode.Created, ActiveSubscriptionJson);
            return Json(HttpStatusCode.NotFound, "");
        });
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(UserReference, UserReference, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(55, result.Subscription.Id);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(new DateTimeOffset(2026, 9, 26, 0, 0, 0, TimeSpan.Zero), result.Subscription.NextBillingDate);
        Assert.Equal(1, handler.Requests.Count(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("/subscriptions.json")));
        Assert.Equal(1, handler.Requests.Count(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("/customers.json")));
    }

    [Fact]
    public async Task SubscribeAsync_WhenActiveSubscriptionExists_ReturnsExistingWithoutPosting()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/customers/lookup.json")) return Json(HttpStatusCode.OK, CustomerJson);
            if (path.Contains("/customers/123/subscriptions.json")) return Json(HttpStatusCode.OK, $"[{ActiveSubscriptionJson}]");
            if (path.Contains("/product_families/")) return Json(HttpStatusCode.OK, PlansJson);
            return Json(HttpStatusCode.NotFound, "");
        });
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(UserReference, UserReference, "eshop-pro");

        Assert.False(result.Created);
        Assert.Equal(55, result.Subscription.Id);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("/subscriptions.json"));
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("/customers.json"));
    }

    [Fact]
    public async Task SubscribeAsync_WhenPlanUnknown_ThrowsBadRequest()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, PlansJson));
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<BillingException>(
            () => service.SubscribeAsync(UserReference, UserReference, "no-such-plan"));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task SubscribeAsync_WhenProviderRejectsSubscription_ThrowsBillingExceptionWith422()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/customers/lookup.json")) return Json(HttpStatusCode.OK, CustomerJson);
            if (path.Contains("/customers/123/subscriptions.json")) return Json(HttpStatusCode.OK, "[]");
            if (path.Contains("/product_families/")) return Json(HttpStatusCode.OK, PlansJson);
            if (path.Contains("/subscriptions.json")) return Json(HttpStatusCode.UnprocessableEntity, """{ "errors": ["Product could not be found"] }""");
            return Json(HttpStatusCode.NotFound, "");
        });
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<BillingException>(
            () => service.SubscribeAsync(UserReference, UserReference, "eshop-pro"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, ex.StatusCode);
        Assert.Contains("Product could not be found", ex.Message);
    }

    [Fact]
    public async Task ListMySubscriptionsAsync_WhenNoCustomer_ReturnsEmpty()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.NotFound, ""));
        var service = CreateService(handler);

        var subscriptions = await service.ListMySubscriptionsAsync(UserReference);

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task ListMySubscriptionsAsync_ReturnsMappedSubscriptions()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/customers/lookup.json")) return Json(HttpStatusCode.OK, CustomerJson);
            if (path.Contains("/customers/123/subscriptions.json")) return Json(HttpStatusCode.OK, $"[{ActiveSubscriptionJson}]");
            return Json(HttpStatusCode.NotFound, "");
        });
        var service = CreateService(handler);

        var subscriptions = await service.ListMySubscriptionsAsync(UserReference);

        var sub = Assert.Single(subscriptions);
        Assert.Equal(55, sub.Id);
        Assert.Equal("Pro Plan", sub.PlanName);
        Assert.Equal(29900, sub.PriceInCents);
        Assert.Equal("active", sub.State);
        Assert.Equal(new DateTimeOffset(2026, 9, 26, 0, 0, 0, TimeSpan.Zero), sub.NextBillingDate);
    }
}
