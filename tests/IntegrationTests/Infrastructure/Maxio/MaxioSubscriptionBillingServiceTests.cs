using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Infrastructure.Maxio;

public class MaxioSubscriptionBillingServiceTests
{
    // Responds by call ORDER rather than matching the request URL, since the exact wire paths are
    // an SDK implementation detail this test doesn't need to know - only the call sequence each
    // MaxioSubscriptionBillingService method makes.
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<int, HttpResponseMessage> _responder;
        private int _callCount;

        // The SDK disposes each HttpRequestMessage (and its content) once the send completes, so the
        // request body must be captured here, not read later from a stored HttpRequestMessage.
        public List<string> RequestBodies { get; } = new();

        public StubHandler(Func<int, HttpResponseMessage> responder) => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct));
            _callCount++;
            return _responder(_callCount);
        }
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    // SubscribeAsync reads the site's default payment-collection method before creating a subscription
    // (these no-trial plans assess a balance immediately, and a site defaulting to "automatic" collection
    // needs a non-automatic override to succeed without a payment profile - see the doc comment on
    // MaxioSubscriptionBillingService.ResolveNonAutomaticCollectionMethodIfNeededAsync).
    private const string AutomaticSiteJson =
        """{ "site": { "relationship_invoicing_enabled": true, "default_payment_collection_method": "automatic" } }""";

    private static MaxioSubscriptionBillingService CreateService(StubHandler handler, string productFamilyHandle = "eshop-subscribe")
    {
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions());
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = productFamilyHandle
        });
        return new MaxioSubscriptionBillingService(client, options);
    }

    [Fact]
    public async Task SubscribeAsync_ExistingCustomer_DoesNotCreateANewCustomer()
    {
        var handler = new StubHandler(callNumber => callNumber switch
        {
            1 => JsonResponse(HttpStatusCode.OK, """{ "customer": { "id": 501, "reference": "buyer@example.com" } }"""),
            2 => JsonResponse(HttpStatusCode.OK, AutomaticSiteJson),
            3 => JsonResponse(HttpStatusCode.OK, """
                { "subscription": { "id": 999, "state": "active", "product_price_in_cents": 29900,
                  "current_period_ends_at": "2026-10-05T00:00:00Z",
                  "product": { "handle": "eshop-pro", "name": "Pro Plan" } } }
                """),
            _ => throw new InvalidOperationException("Unexpected extra request - a double-click must not create a second customer.")
        });

        var service = CreateService(handler);

        var subscription = await service.SubscribeAsync("buyer@example.com", "eshop-pro");

        Assert.Equal(3, handler.RequestBodies.Count);
        Assert.Equal(999, subscription.Id);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal("active", subscription.State);
        Assert.Equal(299.00m, subscription.Price);
        Assert.Equal(DateTimeOffset.Parse("2026-10-05T00:00:00Z"), subscription.NextBillingDate);
    }

    [Fact]
    public async Task SubscribeAsync_NewCustomer_CreatesCustomerWithReferenceThenSubscribes()
    {
        var handler = new StubHandler(callNumber => callNumber switch
        {
            1 => JsonResponse(HttpStatusCode.NotFound, "not found"),
            2 => JsonResponse(HttpStatusCode.Created, """{ "customer": { "id": 777, "reference": "buyer@example.com" } }"""),
            3 => JsonResponse(HttpStatusCode.OK, AutomaticSiteJson),
            4 => JsonResponse(HttpStatusCode.OK, """
                { "subscription": { "id": 1000, "state": "active", "product_price_in_cents": 2900,
                  "current_period_ends_at": "2026-10-05T00:00:00Z",
                  "product": { "handle": "basic-plan", "name": "Basic Plan" } } }
                """),
            _ => throw new InvalidOperationException("Unexpected extra request.")
        });

        var service = CreateService(handler);

        var subscription = await service.SubscribeAsync("buyer@example.com", "basic-plan");

        Assert.Equal(4, handler.RequestBodies.Count);
        Assert.Contains("\"reference\":\"buyer@example.com\"", handler.RequestBodies[1]);
        Assert.Equal(1000, subscription.Id);
        Assert.Equal(29.00m, subscription.Price);
    }

    [Fact]
    public async Task GetSubscriptionsForBuyerAsync_UnknownBuyer_ReturnsEmptyWithoutError()
    {
        var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.NotFound, "not found"));
        var service = CreateService(handler);

        var subscriptions = await service.GetSubscriptionsForBuyerAsync("nobody@example.com");

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task SubscribeAsync_ProviderRejectsSubscription_ThrowsWithProviderStatusAndMessage()
    {
        var handler = new StubHandler(callNumber => callNumber switch
        {
            1 => JsonResponse(HttpStatusCode.OK, """{ "customer": { "id": 501, "reference": "buyer@example.com" } }"""),
            2 => JsonResponse(HttpStatusCode.OK, AutomaticSiteJson),
            3 => JsonResponse(HttpStatusCode.UnprocessableEntity, """{ "errors": ["Product handle is invalid"] }"""),
            _ => throw new InvalidOperationException("Unexpected extra request.")
        });

        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => service.SubscribeAsync("buyer@example.com", "does-not-exist"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, ex.StatusCode);
        Assert.Contains("Product handle is invalid", ex.Message);
    }

    [Fact]
    public async Task GetAvailablePlansAsync_MapsPlansFromResolvedProductFamily()
    {
        var handler = new StubHandler(callNumber => callNumber switch
        {
            1 => JsonResponse(HttpStatusCode.OK,
                """[ { "product_family": { "id": 3023074, "name": "eShop Subscribe", "handle": "eshop-subscribe" } } ]"""),
            2 => JsonResponse(HttpStatusCode.OK, """
                [ { "product": { "id": 7126957, "name": "Pro Plan", "handle": "eshop-pro",
                    "price_in_cents": 29900, "interval": 1, "interval_unit": "month" } },
                  { "product": { "id": 7126958, "name": "Basic Plan", "handle": "basic-plan",
                    "price_in_cents": 2900, "interval": 1, "interval_unit": "month" } } ]
                """),
            _ => throw new InvalidOperationException("Unexpected extra request.")
        });

        var service = CreateService(handler);

        var plans = await service.GetAvailablePlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Contains(plans, p => p.Handle == "eshop-pro" && p.Price == 299.00m);
        Assert.Contains(plans, p => p.Handle == "basic-plan" && p.Price == 29.00m);
    }
}
