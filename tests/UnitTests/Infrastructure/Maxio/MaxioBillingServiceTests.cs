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
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioBillingServiceTests
{
    private const string CustomerJson = """
        {"customer":{"id":123,"first_name":"Demo","last_name":"User","email":"demouser@test.com","reference":"eshop-user-1","created_at":"2026-01-01T00:00:00-04:00","updated_at":"2026-01-01T00:00:00-04:00"}}
        """;

    private const string SubscriptionJson = """
        {"subscription":{"id":456,"state":"active","reference":"eshop-user-1-eshop-pro","current_period_ends_at":"2026-10-09T00:00:00-04:00","product_price_in_cents":29900,"product":{"id":7126957,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month","product_family":{"id":3023074,"handle":"eshop-subscribe","name":"eShop Subscribe"}},"customer":{"id":123,"reference":"eshop-user-1"}}}
        """;

    private const string ProductsJson = """
        [{"product":{"id":7126957,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,"description":"The pro plan","product_family":{"id":3023074,"handle":"eshop-subscribe","name":"eShop Subscribe"}}},
         {"product":{"id":7126958,"handle":"basic-plan","name":"Basic Plan","price_in_cents":2900,"interval":1,"interval_unit":"month","require_credit_card":false,"product_family":{"id":3023074,"handle":"eshop-subscribe","name":"eShop Subscribe"}}}]
        """;

    private static string LookupPath => "/customers/lookup";

    private static (MaxioBillingService Service, StubMaxioHandler Handler) CreateService()
    {
        var handler = new StubMaxioHandler();
        var options = new MaxioOptions
        {
            ApiKey = "test-api-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "eshop-subscribe"
        };
        var client = new MaxioAdvancedBillingClient(
            new HttpClient(handler), MaxioServiceCollectionExtensions.BuildClientOptions(options));
        return (new MaxioBillingService(client, options, NullLogger<MaxioBillingService>.Instance), handler);
    }

    [Fact]
    public async Task SubscribeAsyncCreatesCustomerAndSubscription()
    {
        var (service, handler) = CreateService();
        handler.EnqueueJson(HttpStatusCode.NotFound, """{"errors":["Not found"]}""");   // customer lookup -> miss
        handler.EnqueueJson(HttpStatusCode.OK, CustomerJson);                            // customer create
        handler.EnqueueJson(HttpStatusCode.NotFound, """{"errors":["Not found"]}""");   // subscription find -> miss
        handler.EnqueueJson(HttpStatusCode.OK, SubscriptionJson);                        // subscription create

        var result = await service.SubscribeAsync("1", "demouser@test.com", "eshop-pro");

        Assert.Equal(456, result.SubscriptionId);
        Assert.Equal("eshop-pro", result.PlanHandle);
        Assert.Equal("Pro Plan", result.PlanName);
        Assert.Equal("active", result.State);
        Assert.Equal(299m, result.Price);
        Assert.NotNull(result.NextBillingDate);
        Assert.Equal(4, handler.Requests.Count);

        var createBody = handler.Bodies[^1];
        Assert.Contains("\"product_handle\":\"eshop-pro\"", createBody);
        Assert.Contains("\"customer_id\":123", createBody);
        Assert.Contains("\"reference\":\"eshop-user-1-eshop-pro\"", createBody);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", createBody);

        var createCustomerBody = handler.Bodies[1];
        Assert.Contains("\"reference\":\"eshop-user-1\"", createCustomerBody);
        Assert.Contains("\"email\":\"demouser@test.com\"", createCustomerBody);
    }

    [Fact]
    public async Task SubscribeAsyncWhenAlreadySubscribedReturnsExistingWithoutCreating()
    {
        var (service, handler) = CreateService();
        handler.EnqueueJson(HttpStatusCode.OK, CustomerJson);         // customer lookup -> hit
        handler.EnqueueJson(HttpStatusCode.OK, SubscriptionJson);     // subscription find -> hit

        var result = await service.SubscribeAsync("1", "demouser@test.com", "eshop-pro");

        Assert.Equal(456, result.SubscriptionId);
        Assert.Equal(2, handler.Requests.Count);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task SubscribeAsyncCustomerCreateRaceReusesExistingCustomer()
    {
        var (service, handler) = CreateService();
        handler.EnqueueJson(HttpStatusCode.NotFound, """{"errors":["Not found"]}""");                  // customer lookup -> miss
        handler.EnqueueJson(HttpStatusCode.UnprocessableEntity, """{"errors":["Reference has already been taken"]}"""); // create -> 422 (lost race)
        handler.EnqueueJson(HttpStatusCode.OK, CustomerJson);                                          // re-lookup -> hit
        handler.EnqueueJson(HttpStatusCode.NotFound, """{"errors":["Not found"]}""");                  // subscription find -> miss
        handler.EnqueueJson(HttpStatusCode.OK, SubscriptionJson);                                      // subscription create

        var result = await service.SubscribeAsync("1", "demouser@test.com", "eshop-pro");

        Assert.Equal(456, result.SubscriptionId);
        Assert.Equal(5, handler.Requests.Count);
    }

    [Fact]
    public async Task SubscribeAsyncWithUnknownPlanSurfaces422()
    {
        var (service, handler) = CreateService();
        handler.EnqueueJson(HttpStatusCode.OK, CustomerJson);         // customer lookup -> hit
        handler.EnqueueJson(HttpStatusCode.NotFound, """{"errors":["Not found"]}""");   // subscription find -> miss
        handler.EnqueueJson(HttpStatusCode.UnprocessableEntity, """{"errors":["Invalid product: bad-plan"]}"""); // create -> 422
        handler.EnqueueJson(HttpStatusCode.NotFound, """{"errors":["Not found"]}""");   // reconcile find -> miss

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(
            () => service.SubscribeAsync("1", "demouser@test.com", "bad-plan"));

        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("Invalid product", ex.Message);
    }

    [Fact]
    public async Task ListPlansAsyncMapsProducts()
    {
        var (service, handler) = CreateService();
        handler.EnqueueJson(HttpStatusCode.OK, ProductsJson);

        var plans = await service.ListPlansAsync();

        Assert.Equal(2, plans.Count);
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(299m, pro.Price);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.False(pro.RequireCreditCard);
        Assert.Contains("eshop-subscribe", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task GetUserSubscriptionsWithoutMaxioCustomerReturnsEmpty()
    {
        var (service, handler) = CreateService();
        handler.EnqueueJson(HttpStatusCode.NotFound, """{"errors":["Not found"]}""");

        var subscriptions = await service.GetUserSubscriptionsAsync("1");

        Assert.Empty(subscriptions);
        Assert.Equal(1, handler.Requests.Count);
        Assert.Contains(LookupPath, handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetUserSubscriptionsMapsSubscriptions()
    {
        var (service, handler) = CreateService();
        handler.EnqueueJson(HttpStatusCode.OK, CustomerJson);
        handler.EnqueueJson(HttpStatusCode.OK, $"[{SubscriptionJson}]");

        var subscriptions = await service.GetUserSubscriptionsAsync("1");

        var subscription = Assert.Single(subscriptions);
        Assert.Equal(456, subscription.SubscriptionId);
        Assert.Equal("eshop-user-1-eshop-pro", subscription.Reference);
        Assert.Equal("active", subscription.State);
        Assert.Equal(299m, subscription.Price);
        Assert.NotNull(subscription.NextBillingDate);
    }

    private sealed class StubMaxioHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responders = new();

        public List<HttpRequestMessage> Requests { get; } = new();

        /// <summary>Request bodies, captured at send time (the SDK disposes the content afterwards).</summary>
        public List<string> Bodies { get; } = new();

        public void EnqueueJson(HttpStatusCode status, string json) =>
            _responders.Enqueue(_ => new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct));
            if (_responders.Count == 0)
            {
                throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
            }
            return _responders.Dequeue()(request);
        }
    }
}
