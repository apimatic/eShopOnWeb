using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

/// <summary>
/// Exercises <see cref="MaxioBillingService"/> against the SDK's HTTP seam (a stub
/// <see cref="HttpMessageHandler"/>) — no network. These lock down the create path, the idempotent
/// dedupe path, plan mapping, and provider-error translation deterministically.
/// </summary>
public class MaxioBillingServiceTests
{
    private static readonly BillingUserIdentity User = new("newuser@example.com", "newuser@example.com");

    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string, (HttpStatusCode Status, string Json)> _route;
        public List<(HttpMethod Method, string Path, string Body)> Calls { get; } = new();

        public RoutingHandler(Func<HttpRequestMessage, string, (HttpStatusCode, string)> route) => _route = route;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request.Method, request.RequestUri!.AbsolutePath, body));
            var (status, json) = _route(request, body);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
                RequestMessage = request
            };
        }
    }

    private static MaxioBillingService BuildService(RoutingHandler handler)
    {
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions());
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "test-family"
        });
        return new MaxioBillingService(client, settings, NullLogger<MaxioBillingService>.Instance);
    }

    private const string ActiveProSubscriptionJson =
        """{"subscription":{"id":555,"state":"active","product":{"handle":"eshop-pro","name":"Pro Plan"},"product_price_in_cents":29900,"currency":"USD","current_period_ends_at":"2026-10-09T00:00:00+00:00","next_assessment_at":"2026-10-09T00:00:00+00:00","created_at":"2026-09-09T00:00:00+00:00"}}""";

    [Fact]
    public async Task SubscribeAsync_creates_customer_and_subscription_when_none_exist()
    {
        var handler = new RoutingHandler((req, _) =>
        {
            string path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Get && path.Contains("/customers/lookup"))
                return (HttpStatusCode.NotFound, "{}"); // customer does not exist yet
            if (req.Method == HttpMethod.Post && path.EndsWith("/customers.json"))
                return (HttpStatusCode.Created, """{"customer":{"id":123,"reference":"newuser@example.com","email":"newuser@example.com"}}""");
            if (req.Method == HttpMethod.Get && path.Contains("/customers/123/subscriptions"))
                return (HttpStatusCode.OK, "[]"); // no existing subscriptions
            if (req.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
                return (HttpStatusCode.Created, """{"subscription":{"id":777,"state":"active","product":{"handle":"eshop-pro","name":"Pro Plan"},"product_price_in_cents":29900,"currency":"USD","current_period_ends_at":"2026-10-09T00:00:00+00:00","next_assessment_at":"2026-10-09T00:00:00+00:00","created_at":"2026-09-09T00:00:00+00:00"}}""");
            return (HttpStatusCode.InternalServerError, "{}");
        });
        var service = BuildService(handler);

        var result = await service.SubscribeAsync(User, "eshop-pro", CancellationToken.None);

        Assert.False(result.AlreadyExisted);
        Assert.Equal(777, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal(29900, result.Subscription.PriceInCents);
        Assert.NotNull(result.Subscription.NextBillingAt);

        // A customer was created and a subscription POST carried the plan handle + resolved customer id.
        Assert.Single(handler.Calls.Where(c => c.Method == HttpMethod.Post && c.Path.EndsWith("/customers.json")));
        var createSub = handler.Calls.Single(c => c.Method == HttpMethod.Post && c.Path.EndsWith("/subscriptions.json"));
        Assert.Contains("\"product_handle\":\"eshop-pro\"", createSub.Body);
        Assert.Contains("\"customer_id\":123", createSub.Body);
    }

    [Fact]
    public async Task SubscribeAsync_dedupes_to_existing_active_subscription_without_creating()
    {
        var handler = new RoutingHandler((req, _) =>
        {
            string path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Get && path.Contains("/customers/lookup"))
                return (HttpStatusCode.OK, """{"customer":{"id":123,"reference":"newuser@example.com"}}""");
            if (req.Method == HttpMethod.Get && path.Contains("/customers/123/subscriptions"))
                return (HttpStatusCode.OK, $"[{ActiveProSubscriptionJson}]");
            return (HttpStatusCode.InternalServerError, "{}");
        });
        var service = BuildService(handler);

        var result = await service.SubscribeAsync(User, "eshop-pro", CancellationToken.None);

        Assert.True(result.AlreadyExisted);
        Assert.Equal(555, result.Subscription.Id);
        // No customer and no subscription were created.
        Assert.DoesNotContain(handler.Calls, c => c.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task GetPlansAsync_resolves_family_id_then_maps_products()
    {
        var handler = new RoutingHandler((req, _) =>
        {
            string path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Get && path.EndsWith("/product_families.json"))
                return (HttpStatusCode.OK, """[{"product_family":{"id":3023074,"handle":"test-family","name":"eShop"}}]""");
            if (req.Method == HttpMethod.Get && path.Contains("/product_families/3023074/products"))
                return (HttpStatusCode.OK, """[{"product":{"id":7126957,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}},{"product":{"id":7126958,"handle":"basic-plan","name":"Basic Plan","price_in_cents":2900,"interval":1,"interval_unit":"month"}}]""");
            return (HttpStatusCode.InternalServerError, "{}");
        });
        var service = BuildService(handler);

        var plans = await service.GetPlansAsync(CancellationToken.None);

        Assert.Equal(2, plans.Count);
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal("month", pro.IntervalUnit);
    }

    [Fact]
    public async Task GetSubscriptionsForUserAsync_returns_empty_when_customer_absent()
    {
        var handler = new RoutingHandler((req, _) =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.Contains("/customers/lookup"))
                return (HttpStatusCode.NotFound, "{}");
            return (HttpStatusCode.InternalServerError, "{}");
        });
        var service = BuildService(handler);

        var subs = await service.GetSubscriptionsForUserAsync(User, CancellationToken.None);

        Assert.Empty(subs);
    }

    [Fact]
    public async Task SubscribeAsync_translates_422_on_create_to_caller_error()
    {
        var handler = new RoutingHandler((req, _) =>
        {
            string path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Get && path.Contains("/customers/lookup"))
                return (HttpStatusCode.OK, """{"customer":{"id":123}}""");
            if (req.Method == HttpMethod.Get && path.Contains("/customers/123/subscriptions"))
                return (HttpStatusCode.OK, "[]");
            if (req.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
                return ((HttpStatusCode)422, """{"errors":["Product: does not exist."]}""");
            return (HttpStatusCode.InternalServerError, "{}");
        });
        var service = BuildService(handler);

        var ex = await Assert.ThrowsAsync<BillingException>(
            () => service.SubscribeAsync(User, "no-such-plan", CancellationToken.None));

        Assert.True(ex.IsCallerError);
        Assert.Equal(422, ex.ProviderStatusCode);
        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public async Task SubscribeAsync_translates_transport_failure_to_provider_unavailable()
    {
        var handler = new RoutingHandler((_, _) => throw new HttpRequestException("connection reset"));
        var service = BuildService(handler);

        var ex = await Assert.ThrowsAsync<BillingException>(
            () => service.SubscribeAsync(User, "eshop-pro", CancellationToken.None));

        Assert.False(ex.IsCallerError); // provider unavailable, not the caller's fault
    }
}
