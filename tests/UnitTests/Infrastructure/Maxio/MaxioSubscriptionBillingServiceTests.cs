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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Exercises the Maxio-backed billing service through the SDK's HttpClient seam (a stub handler),
/// so no network is touched. Focuses on the idempotency guarantees the endpoints promise.
/// </summary>
public class MaxioSubscriptionBillingServiceTests
{
    private const string ProductsJson =
        """[{"product":{"id":10,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]""";

    private const string CustomerJson =
        """{"customer":{"id":123,"reference":"demouser@microsoft.com","email":"demouser@microsoft.com","first_name":"Demo","last_name":"User"}}""";

    private const string ActiveProSubJson =
        """[{"subscription":{"id":999,"state":"active","current_period_ends_at":"2026-09-16T00:00:00Z","product_price_in_cents":29900,"product":{"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900}}}]""";

    private const string CreatedSubJson =
        """{"subscription":{"id":1000,"state":"active","current_period_ends_at":"2026-09-16T00:00:00Z","product_price_in_cents":29900,"product":{"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900}}}""";

    private static readonly SubscriberIdentity Subscriber =
        new("demouser@microsoft.com", "demouser@microsoft.com", "Demo", "User");

    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();

        public RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public int PostCount(string pathContains) =>
            Requests.Count(r => r.Method == HttpMethod.Post && (r.RequestUri?.AbsolutePath.Contains(pathContains) ?? false));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static MaxioSubscriptionBillingService BuildService(RoutingHandler handler)
    {
        var httpClient = new HttpClient(new MaxioSingleWriteAttemptHandler { InnerHandler = handler });
        var options = new MaxioAdvancedBillingClientOptions();
        options.Server.Production.Us.Site = "test";
        var client = new MaxioAdvancedBillingClient(httpClient, options);

        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "k",
            Subdomain = "test",
            ProductFamilyHandle = "eshop-subscribe",
            Environment = "US",
        });

        var logger = Substitute.For<IAppLogger<MaxioSubscriptionBillingService>>();
        return new MaxioSubscriptionBillingService(client, settings, logger);
    }

    // Routes GET/POST by path. GET on /customers/{id}/subscriptions.json contains BOTH "customers"
    // and "subscriptions", so subscriptions is checked first.
    private static Func<HttpRequestMessage, HttpResponseMessage> Router(
        string customerLookup, HttpStatusCode customerLookupStatus,
        string subscriptionsList,
        Func<HttpResponseMessage>? onCreateSubscription = null)
    {
        return req =>
        {
            var path = req.RequestUri?.AbsolutePath ?? string.Empty;
            var isGet = req.Method == HttpMethod.Get;
            var isPost = req.Method == HttpMethod.Post;

            if (isGet && path.Contains("subscriptions")) return Json(HttpStatusCode.OK, subscriptionsList);
            if (isGet && path.Contains("products")) return Json(HttpStatusCode.OK, ProductsJson);
            if (isGet && path.Contains("customers")) return Json(customerLookupStatus, customerLookup);
            if (isPost && path.Contains("subscriptions")) return (onCreateSubscription ?? (() => Json(HttpStatusCode.Created, CreatedSubJson)))();
            if (isPost && path.Contains("customers")) return Json(HttpStatusCode.Created, CustomerJson);

            return Json(HttpStatusCode.InternalServerError, "{}");
        };
    }

    [Fact]
    public async Task Subscribe_WhenAlreadySubscribedToPlan_ReturnsExisting_AndWritesNothing()
    {
        var handler = new RoutingHandler(Router(CustomerJson, HttpStatusCode.OK, ActiveProSubJson));
        var service = BuildService(handler);

        var result = await service.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.True(result.AlreadyExisted);
        Assert.Equal(999, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(0, handler.PostCount("subscriptions"));
        Assert.Equal(0, handler.PostCount("customers"));
    }

    [Fact]
    public async Task Subscribe_WhenNewCustomerAndNoSubscription_CreatesBoth_Once()
    {
        var handler = new RoutingHandler(Router("{}", HttpStatusCode.NotFound, "[]"));
        var service = BuildService(handler);

        var result = await service.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal(1000, result.Subscription.Id);
        Assert.Equal(29900, result.Subscription.PriceInCents);
        Assert.Equal(1, handler.PostCount("customers"));
        Assert.Equal(1, handler.PostCount("subscriptions"));
    }

    [Fact]
    public async Task Subscribe_WhenPlanHandleUnknown_ThrowsPlanNotFound_AndWritesNothing()
    {
        var handler = new RoutingHandler(Router(CustomerJson, HttpStatusCode.OK, "[]"));
        var service = BuildService(handler);

        await Assert.ThrowsAsync<PlanNotFoundException>(() => service.SubscribeAsync(Subscriber, "ghost-plan"));

        Assert.Equal(0, handler.PostCount("subscriptions"));
        Assert.Equal(0, handler.PostCount("customers"));
    }

    [Fact]
    public async Task Subscribe_WhenCreateSubscriptionTransportFaults_SendsWriteAtMostOnce()
    {
        // The create POST always faults at the transport layer. The SDK would retry it on every
        // verb; the write guard must hold it to a single send on the wire.
        var handler = new RoutingHandler(Router(
            CustomerJson, HttpStatusCode.OK, "[]",
            onCreateSubscription: () => throw new HttpRequestException("connection reset")));
        var service = BuildService(handler);

        await Assert.ThrowsAsync<SubscriptionBillingException>(() => service.SubscribeAsync(Subscriber, "eshop-pro"));

        Assert.Equal(1, handler.PostCount("subscriptions"));
    }
}
