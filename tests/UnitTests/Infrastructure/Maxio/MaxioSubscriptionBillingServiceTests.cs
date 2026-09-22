using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Integration-layer tests for <see cref="MaxioSubscriptionBillingService"/>. The SDK's HttpClient
/// constructor argument is the test seam — a stub handler returns canned wire JSON so no network call
/// happens (per the dotnet-testing skill).
/// </summary>
public class MaxioSubscriptionBillingServiceTests
{
    private static MaxioSubscriptionBillingService BuildService(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        IRepository<BuyerSubscription>? repository = null)
    {
        var client = new MaxioAdvancedBillingClient(
            new HttpClient(new StubHandler(responder)),
            new MaxioAdvancedBillingClientOptions());

        var settings = new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test-sub",
            ProductFamilyHandle = "eshop-subscribe"
        };

        return new MaxioSubscriptionBillingService(
            client,
            repository ?? Substitute.For<IRepository<BuyerSubscription>>(),
            settings,
            NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    [Fact]
    public async Task GetPlansAsync_MapsProductsAndFiltersArchived()
    {
        var service = BuildService(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            // ListProductsForProductFamily → /product_families/{id}/products.json
            if (path.Contains("/products.json"))
            {
                return Json("""
                [
                  { "product": { "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900,
                                 "interval": 1, "interval_unit": "month", "require_credit_card": false } },
                  { "product": { "handle": "retired", "name": "Retired", "price_in_cents": 100,
                                 "archived_at": "2020-01-01T00:00:00Z" } }
                ]
                """);
            }

            // ListProductFamilies → /product_families.json
            return Json("""[ { "product_family": { "id": 42, "handle": "eshop-subscribe" } } ]""");
        });

        var result = await service.GetPlansAsync(CancellationToken.None);

        Assert.False(result.Truncated);
        var plan = Assert.Single(result.Plans);          // the archived product is filtered out
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal("month", plan.IntervalUnit);
        Assert.False(plan.PaymentMethodRequired);
    }

    [Fact]
    public async Task GetPlansAsync_TranslatesProviderErrorToProviderUnavailable()
    {
        // ListProductFamilies (Case B, RawError) returns 500 → provider-unavailable, not a leaked SDK type.
        var service = BuildService(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("boom", Encoding.UTF8, "text/plain")
            });

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => service.GetPlansAsync(CancellationToken.None));

        Assert.Equal(BillingErrorKind.ProviderUnavailable, ex.Kind);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_ReturnsEmptyWhenNoCustomer()
    {
        // ReadCustomerByReference 404 → the buyer has no Maxio customer yet → empty list, no error.
        var service = BuildService(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });

        var identity = new SubscriberIdentity("user-1", "user-1@example.com", "User", "One");
        var result = await service.GetSubscriptionsAsync(identity, CancellationToken.None);

        Assert.Empty(result);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = _responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
