using System.Net;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionServiceTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responders;

        public List<HttpRequestMessage> Requests { get; } = new();

        public StubHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responders)
        {
            _responders = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(responders);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var responder = _responders.Count > 0 ? _responders.Dequeue() : _ => new HttpResponseMessage(HttpStatusCode.OK);
            return Task.FromResult(responder(request));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };

    private static MaxioSubscriptionService CreateService(StubHandler handler)
    {
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions());
        var settings = new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "eshop-subscribe"
        };
        return new MaxioSubscriptionService(client, settings, Substitute.For<IAppLogger<MaxioSubscriptionService>>());
    }

    private static readonly ShopperIdentity Shopper =
        new ShopperIdentity("demouser@microsoft.com", "demouser@microsoft.com", "demouser", "Shopper");

    [Fact]
    public async Task ListPlans_ReturnsMappedPlans()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """[{"product":{"id":7126957,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]"""));
        var service = CreateService(handler);

        var plans = await service.ListPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal(1, plan.Interval);
        Assert.Equal("month", plan.IntervalUnit);
        Assert.Contains("product_families", handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Subscribe_WhenCustomerMissing_CreatesCustomerThenSubscription()
    {
        var handler = new StubHandler(
            _ => Json(HttpStatusCode.NotFound, "{}"),
            _ => Json(HttpStatusCode.Created, """{"customer":{"id":123,"reference":"demouser@microsoft.com"}}"""),
            _ => Json(HttpStatusCode.OK, "[]"),
            _ => Json(HttpStatusCode.Created, """{"subscription":{"id":55,"state":"active","product":{"name":"Pro Plan","handle":"eshop-pro"},"product_price_in_cents":29900,"currency":"USD","next_assessment_at":"2026-09-24T00:00:00Z"}}"""));
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(Shopper, "eshop-pro");

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(55, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal("Pro Plan", result.Subscription.ProductName);
        Assert.Equal(29900, result.Subscription.PriceInCents);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero), result.Subscription.NextBillingDate);

        Assert.Equal(4, handler.Requests.Count);
        Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("/customers"));
        Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("/subscriptions"));
    }

    [Fact]
    public async Task Subscribe_WhenLiveSubscriptionExists_ReturnsExistingWithoutCreating()
    {
        var handler = new StubHandler(
            _ => Json(HttpStatusCode.OK, """{"customer":{"id":123,"reference":"demouser@microsoft.com"}}"""),
            _ => Json(HttpStatusCode.OK, """[{"subscription":{"id":55,"state":"active","product":{"name":"Pro Plan","handle":"eshop-pro"},"product_price_in_cents":29900,"currency":"USD","next_assessment_at":"2026-09-24T00:00:00Z"}}]"""));
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(Shopper, "eshop-pro");

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(55, result.Subscription.Id);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task ListSubscriptions_WhenCustomerMissing_ReturnsEmpty()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.NotFound, "{}"));
        var service = CreateService(handler);

        var subscriptions = await service.ListSubscriptionsAsync(Shopper.Username);

        Assert.Empty(subscriptions);
    }
}
