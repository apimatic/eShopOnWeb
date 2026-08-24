using System.Net;
using System.Text;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure;

public class MaxioSubscriptionBillingServiceTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responders;

        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string?> RequestBodies { get; } = new();

        public StubHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responders)
        {
            _responders = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(responders);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            // The SDK disposes request content after the send, so capture the body here.
            RequestBodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(ct));
            if (_responders.Count == 0)
            {
                throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
            }
            return _responders.Dequeue()(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static MaxioSubscriptionBillingService CreateService(StubHandler handler, string productFamilyHandle = "eshop-subscribe")
    {
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions());
        var settings = Options.Create(new MaxioSettings { ProductFamilyHandle = productFamilyHandle });
        return new MaxioSubscriptionBillingService(client, settings, Substitute.For<IAppLogger<MaxioSubscriptionBillingService>>());
    }

    private const string FamiliesJson =
        """[{"product_family": {"id": 3023074, "handle": "eshop-subscribe", "name": "eShop Subscribe"}}]""";

    private const string ProductsJson =
        """[{"product": {"id": 7126957, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month", "archived_at": null}}, {"product": {"id": 7126958, "name": "Basic Plan", "handle": "basic-plan", "price_in_cents": 2900, "interval": 1, "interval_unit": "month", "archived_at": null}}]""";

    private const string CustomerJson =
        """{"customer": {"id": 123, "reference": "user-1", "email": "demouser@microsoft.com", "first_name": "Demouser", "last_name": "Customer"}}""";

    private const string SubscriptionJson =
        """{"subscription": {"id": 555, "state": "active", "reference": "user-1:eshop-pro", "product_price_in_cents": 29900, "next_assessment_at": "2026-09-25T00:00:00+00:00", "product": {"handle": "eshop-pro", "name": "Pro Plan"}, "customer": {"id": 123, "reference": "user-1"}}}""";

    [Fact]
    public async Task GetPlansAsync_ReturnsMappedPlans()
    {
        var handler = new StubHandler(
            _ => Json(HttpStatusCode.OK, FamiliesJson),
            _ => Json(HttpStatusCode.OK, ProductsJson));
        var service = CreateService(handler);

        var plans = await service.GetPlansAsync();

        Assert.Equal(2, plans.Count);
        var pro = Assert.Single(plans, p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(299.00m, pro.Price);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription_WhenNoneExist()
    {
        var handler = new StubHandler(
            _ => Json(HttpStatusCode.NotFound, "{}"),          // customer lookup: miss
            _ => Json(HttpStatusCode.Created, CustomerJson),   // customer create
            _ => Json(HttpStatusCode.NotFound, "{}"),          // find subscription: miss
            _ => Json(HttpStatusCode.Created, SubscriptionJson)); // subscription create
        var service = CreateService(handler);

        var result = await service.SubscribeAsync("user-1", "demouser@microsoft.com", "Demouser", "Customer", "eshop-pro");

        Assert.Equal(555, result.SubscriptionId);
        Assert.Equal("active", result.State);
        Assert.Equal("eshop-pro", result.PlanHandle);
        Assert.Equal("Pro Plan", result.PlanName);
        Assert.Equal(299.00m, result.Price);
        Assert.Equal(DateTimeOffset.Parse("2026-09-25T00:00:00+00:00"), result.NextBillingDate);

        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal(HttpMethod.Post, handler.Requests[3].Method);
        var sentJson = handler.RequestBodies[3]!;
        Assert.Contains("\"product_handle\":\"eshop-pro\"", sentJson);
        Assert.Contains("\"customer_id\":123", sentJson);
        Assert.Contains("\"reference\":\"user-1:eshop-pro\"", sentJson);
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsExistingSubscription_WithoutCreating_WhenAlreadySubscribed()
    {
        var handler = new StubHandler(
            _ => Json(HttpStatusCode.OK, CustomerJson),        // customer lookup: hit
            _ => Json(HttpStatusCode.OK, SubscriptionJson));   // find subscription: hit
        var service = CreateService(handler);

        var result = await service.SubscribeAsync("user-1", "demouser@microsoft.com", "Demouser", "Customer", "eshop-pro");

        Assert.Equal(555, result.SubscriptionId);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_ReturnsEmpty_WhenUserHasNoBillingCustomer()
    {
        var handler = new StubHandler(
            _ => Json(HttpStatusCode.NotFound, "{}"));         // customer lookup: miss
        var service = CreateService(handler);

        var result = await service.GetSubscriptionsAsync("user-1");

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_ReturnsMappedSubscriptions()
    {
        var handler = new StubHandler(
            _ => Json(HttpStatusCode.OK, CustomerJson),                      // customer lookup: hit
            _ => Json(HttpStatusCode.OK, $"[{SubscriptionJson}]"));          // list subscriptions
        var service = CreateService(handler);

        var result = await service.GetSubscriptionsAsync("user-1");

        var sub = Assert.Single(result);
        Assert.Equal(555, sub.SubscriptionId);
        Assert.Equal("active", sub.State);
        Assert.Equal("eshop-pro", sub.PlanHandle);
    }

    [Fact]
    public async Task SubscribeAsync_ThrowsBillingException422_WhenProviderRejectsCreate()
    {
        var handler = new StubHandler(
            _ => Json(HttpStatusCode.OK, CustomerJson),                              // customer lookup: hit
            _ => Json(HttpStatusCode.NotFound, "{}"),                                // find subscription: miss
            _ => Json(HttpStatusCode.UnprocessableEntity, """{"errors": ["Product: could not be found"]}"""), // create: rejected
            _ => Json(HttpStatusCode.NotFound, "{}"));                               // idempotency re-read: still miss
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<BillingException>(
            () => service.SubscribeAsync("user-1", "demouser@microsoft.com", "Demouser", "Customer", "eshop-pro"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, ex.StatusCode);
        Assert.Contains("Product: could not be found", ex.Message);
    }
}
