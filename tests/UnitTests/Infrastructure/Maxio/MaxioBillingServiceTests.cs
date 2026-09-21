using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Tests the Maxio integration through the SDK's real deserialization/error pipeline, faking only the
/// transport (the <see cref="HttpClient"/> seam). This exercises the mapping, idempotency and error
/// translation without touching the network.
/// </summary>
public class MaxioBillingServiceTests
{
    private const string ProductsJson =
        "[{\"product\":{\"id\":1,\"name\":\"Pro Plan\",\"handle\":\"eshop-pro\",\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\",\"require_credit_card\":false}}," +
        "{\"product\":{\"id\":2,\"name\":\"Basic Plan\",\"handle\":\"basic-plan\",\"price_in_cents\":2900,\"interval\":1,\"interval_unit\":\"month\",\"require_credit_card\":false}}]";

    private const string CustomerJson = "{\"customer\":{\"id\":42,\"reference\":\"user@example.com\",\"email\":\"user@example.com\"}}";

    private const string ExistingSubscriptionJson =
        "{\"subscription\":{\"id\":100,\"state\":\"active\",\"product\":{\"handle\":\"eshop-pro\",\"name\":\"Pro Plan\"}," +
        "\"product_price_in_cents\":29900,\"current_period_ends_at\":\"2026-10-22T00:00:00Z\",\"reference\":\"eshop:user@example.com:eshop-pro\"}}";

    private static MaxioBillingService CreateService(RoutingHandler handler)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "k", Password = "x" },
            Environment = ServerEnvironment.Us,
            // Disable retries so a stubbed 5xx surfaces immediately (no multi-second backoff in tests).
            Retry = RetryOptions.Disabled()
        };
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "k",
            Subdomain = "sub",
            ProductFamilyHandle = "fam",
            DefaultPlanHandle = "eshop-pro"
        });
        var logger = Substitute.For<IAppLogger<MaxioBillingService>>();
        return new MaxioBillingService(client, settings, logger);
    }

    [Fact]
    public async Task GetPlansAsync_MapsProductsFromTheFamily()
    {
        var handler = new RoutingHandler(req =>
            req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.Contains("/products.json")
                ? (HttpStatusCode.OK, ProductsJson)
                : (HttpStatusCode.InternalServerError, "{}"));

        var service = CreateService(handler);

        var plans = await service.GetPlansAsync(CancellationToken.None);

        Assert.Equal(2, plans.Count);
        var pro = Assert.Single(plans, p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal("$299.00", pro.PriceFormatted);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.False(pro.RequiresPaymentMethod);
        // The family-products route is addressed by handle prefix (the SDK URL-escapes the ':' to %3A).
        Assert.Contains(handler.Requests, r => r.Path.Contains("/product_families/handle%3Afam/products.json"));
    }

    [Fact]
    public async Task GetSubscriptionsAsync_ReturnsEmpty_WhenCustomerDoesNotExist()
    {
        var handler = new RoutingHandler(req =>
            req.RequestUri!.AbsolutePath.Contains("/customers/lookup.json")
                ? (HttpStatusCode.NotFound, "{\"errors\":[\"not found\"]}")
                : (HttpStatusCode.InternalServerError, "{}"));

        var service = CreateService(handler);

        var subs = await service.GetSubscriptionsAsync("nobody@example.com", CancellationToken.None);

        Assert.Empty(subs);
        // A missing customer must NOT trigger a subscriptions listing call.
        Assert.Single(handler.Requests);
        Assert.Contains(handler.Requests, r => r.Path.Contains("/customers/lookup.json"));
    }

    [Fact]
    public async Task SubscribeAsync_IsIdempotent_ReusesExistingSubscription_WithoutCreating()
    {
        var handler = new RoutingHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Get && path.Contains("/products.json"))
                return (HttpStatusCode.OK, ProductsJson);
            if (req.Method == HttpMethod.Get && path.Contains("/customers/lookup.json"))
                return (HttpStatusCode.OK, CustomerJson);
            if (req.Method == HttpMethod.Get && path.Contains("/subscriptions/lookup.json"))
                return (HttpStatusCode.OK, ExistingSubscriptionJson);
            return (HttpStatusCode.InternalServerError, "{}"); // any POST/unexpected
        });

        var service = CreateService(handler);

        var outcome = await service.SubscribeAsync("user@example.com", "user@example.com", "eshop-pro", CancellationToken.None);

        Assert.True(outcome.AlreadyExisted);
        Assert.Equal(42, outcome.CustomerId);
        Assert.Equal(100, outcome.Subscription.SubscriptionId);
        Assert.Equal("active", outcome.Subscription.State);
        Assert.True(outcome.Subscription.IsActive);
        Assert.Equal("eshop-pro", outcome.Subscription.PlanHandle);
        // The core idempotency guarantee: no create (POST) was issued.
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task GetPlansAsync_MapsProviderFailure_ToProviderUnavailable()
    {
        var handler = new RoutingHandler(_ => (HttpStatusCode.InternalServerError, "{}"));
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => service.GetPlansAsync(CancellationToken.None));

        Assert.Equal(SubscriptionBillingErrorKind.ProviderUnavailable, ex.Kind);
        Assert.Equal(500, ex.ProviderStatusCode);
    }

    /// <summary>
    /// Fakes the transport, routing each request to a canned (status, json) response and recording every
    /// request's method + path so tests can assert what the SDK actually sent (e.g. that no POST occurred).
    /// </summary>
    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly System.Func<HttpRequestMessage, (HttpStatusCode Status, string Json)> _route;

        public List<(HttpMethod Method, string Path)> Requests { get; } = new();

        public RoutingHandler(System.Func<HttpRequestMessage, (HttpStatusCode, string)> route) => _route = route;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath));
            var (status, json) = _route(request);
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
