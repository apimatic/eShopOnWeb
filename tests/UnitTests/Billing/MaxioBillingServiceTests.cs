using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Billing;

/// <summary>
/// Unit tests for the Maxio billing service, exercised over a stubbed
/// HttpMessageHandler (the SDK client's HttpClient seam). Responses are routed
/// in the deterministic order the service issues calls; request assertions
/// check what the SDK actually put on the wire.
/// </summary>
public class MaxioBillingServiceTests
{
    private sealed class RecordedRequest
    {
        public HttpMethod Method { get; init; } = HttpMethod.Get;
        public Uri? Uri { get; init; }
        public string Body { get; init; } = string.Empty;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();
        public List<RecordedRequest> Requests { get; } = new();

        public void Enqueue(HttpStatusCode status, string? json = null) =>
            _responses.Enqueue(_ => Respond(status, json));

        public void EnqueueJson(HttpStatusCode status, string json) =>
            _responses.Enqueue(_ => Respond(status, json));

        private static HttpResponseMessage Respond(HttpStatusCode status, string? json)
        {
            var response = new HttpResponseMessage(status);
            if (json != null)
            {
                response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }
            return response;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(ct);
            Requests.Add(new RecordedRequest { Method = request.Method, Uri = request.RequestUri, Body = body });
            return Task.FromResult(
                _responses.Count > 0
                    ? _responses.Dequeue()(request)
                    : new HttpResponseMessage(HttpStatusCode.InternalServerError)).Result;
        }
    }

    private static MaxioBillingService CreateService(StubHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpClient(MaxioServiceCollectionExtensions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "eshop-subscribe"
        });
        return new MaxioBillingService(factory, options, NullLogger<MaxioBillingService>.Instance);
    }

    private const string FamiliesJson = """
        [{ "product_family": { "id": 1, "handle": "eshop-subscribe", "name": "eShop Subscribe", "archived_at": null } }]
        """;

    private const string ProductsJson = """
        [
          { "product": { "id": 100, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900,
                         "interval": 1, "interval_unit": "month", "require_credit_card": false,
                         "request_credit_card": false, "archived_at": null, "description": "The pro plan" } },
          { "product": { "id": 101, "name": "Basic Plan", "handle": "basic-plan", "price_in_cents": 2900,
                         "interval": 1, "interval_unit": "month", "require_credit_card": false,
                         "request_credit_card": false, "archived_at": null, "description": null } }
        ]
        """;

    private const string SubscriptionJson = """
        { "subscription": { "id": 900, "state": "active", "reference": "eshop-user1-eshop-pro",
                            "product_price_in_cents": 29900, "balance_in_cents": 0,
                            "current_period_started_at": "2026-09-09T00:00:00Z",
                            "current_period_ends_at": "2026-10-09T00:00:00Z",
                            "next_assessment_at": "2026-10-09T00:00:00Z",
                            "product": { "id": 100, "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900 },
                            "customer": { "id": 555, "reference": "eshop-user1" } } }
        """;

    private const string CustomerJson = """
        { "customer": { "id": 555, "reference": "eshop-user1", "first_name": "eShop",
                        "last_name": "Shopper", "email": "user1@example.com" } }
        """;

    [Fact]
    public async Task ListPlans_MapsFamilyProducts()
    {
        var handler = new StubHandler();
        handler.EnqueueJson(HttpStatusCode.OK, FamiliesJson);
        handler.EnqueueJson(HttpStatusCode.OK, ProductsJson);
        var service = CreateService(handler);

        var plans = await service.ListPlansAsync(default);

        Assert.Equal(2, plans.Count);
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(299m, pro.Price);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.False(pro.RequireCreditCard);
        Assert.All(handler.Requests, r => Assert.Equal(HttpMethod.Get, r.Method));
    }

    [Fact]
    public async Task Subscribe_HappyPath_CreatesCustomerAndSubscription()
    {
        var handler = new StubHandler();
        handler.EnqueueJson(HttpStatusCode.OK, FamiliesJson);        // resolve family
        handler.EnqueueJson(HttpStatusCode.OK, ProductsJson);        // validate plan
        handler.Enqueue(HttpStatusCode.NotFound, "{}");              // customer by reference -> not found
        handler.EnqueueJson(HttpStatusCode.Created, CustomerJson);   // create customer
        handler.Enqueue(HttpStatusCode.NotFound, "{}");              // find subscription -> not found
        handler.EnqueueJson(HttpStatusCode.Created, SubscriptionJson); // create subscription
        var service = CreateService(handler);

        var subscription = await service.SubscribeAsync("user1", "user1@example.com", "eshop-pro", default);

        Assert.Equal(900, subscription.Id);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal("Pro Plan", subscription.PlanName);
        Assert.Equal(299m, subscription.Price);
        Assert.Equal("active", subscription.State);
        Assert.Equal("eshop-user1-eshop-pro", subscription.Reference);
        Assert.NotNull(subscription.NextBillingDate);

        var customerLookup = handler.Requests[2];
        Assert.Equal(HttpMethod.Get, customerLookup.Method);
        Assert.Contains("reference=eshop-user1", customerLookup.Uri!.Query);

        var createCustomer = handler.Requests[3];
        Assert.Equal(HttpMethod.Post, createCustomer.Method);
        Assert.Contains("\"reference\":\"eshop-user1\"", createCustomer.Body);
        Assert.Contains("\"email\":\"user1@example.com\"", createCustomer.Body);

        var createSubscription = handler.Requests[5];
        Assert.Equal(HttpMethod.Post, createSubscription.Method);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", createSubscription.Body);
        Assert.Contains("\"customer_id\":555", createSubscription.Body);
        Assert.Contains("\"reference\":\"eshop-user1-eshop-pro\"", createSubscription.Body);
    }

    [Fact]
    public async Task Subscribe_AlreadySubscribed_ReturnsExistingWithoutCreating()
    {
        var handler = new StubHandler();
        handler.EnqueueJson(HttpStatusCode.OK, FamiliesJson);         // resolve family
        handler.EnqueueJson(HttpStatusCode.OK, ProductsJson);         // validate plan
        handler.EnqueueJson(HttpStatusCode.OK, CustomerJson);         // customer by reference -> found
        handler.EnqueueJson(HttpStatusCode.OK, SubscriptionJson);     // find subscription -> found
        var service = CreateService(handler);

        var subscription = await service.SubscribeAsync("user1", "user1@example.com", "eshop-pro", default);

        Assert.Equal(900, subscription.Id);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Subscribe_ConcurrentCustomerCreate_RaceIsSettledByReference()
    {
        var handler = new StubHandler();
        handler.EnqueueJson(HttpStatusCode.OK, FamiliesJson);        // resolve family
        handler.EnqueueJson(HttpStatusCode.OK, ProductsJson);        // validate plan
        handler.Enqueue(HttpStatusCode.NotFound, "{}");              // customer by reference -> not found
        // The duplicate-reference 422 body does not parse as the typed error
        // shape, so the SDK's error construction throws JsonException and the
        // service must settle the outcome by re-reading.
        handler.Enqueue(HttpStatusCode.UnprocessableEntity, "{ \"errors\": [\"reference has already been taken\"] }");
        handler.EnqueueJson(HttpStatusCode.OK, CustomerJson);        // re-read by reference -> the winner
        handler.Enqueue(HttpStatusCode.NotFound, "{}");              // find subscription -> not found
        handler.EnqueueJson(HttpStatusCode.Created, SubscriptionJson); // create subscription
        var service = CreateService(handler);

        var subscription = await service.SubscribeAsync("user1", "user1@example.com", "eshop-pro", default);

        Assert.Equal(900, subscription.Id);
        var customerPosts = handler.Requests.Where(r => r.Method == HttpMethod.Post && r.Uri!.AbsolutePath.Contains("customer"));
        Assert.Single(customerPosts);
    }

    [Fact]
    public async Task Subscribe_UnknownPlan_ThrowsPlanNotFoundWithoutAnyWrite()
    {
        var handler = new StubHandler();
        handler.EnqueueJson(HttpStatusCode.OK, FamiliesJson);
        handler.EnqueueJson(HttpStatusCode.OK, ProductsJson);
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(
            () => service.SubscribeAsync("user1", "user1@example.com", "no-such-plan", default));

        Assert.Equal(MaxioBillingError.PlanNotFound, ex.Error);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Subscribe_MissingFamily_ThrowsFamilyNotFound()
    {
        var handler = new StubHandler();
        handler.EnqueueJson(HttpStatusCode.OK, "[]");
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(
            () => service.ListPlansAsync(default));

        Assert.Equal(MaxioBillingError.FamilyNotFound, ex.Error);
    }

    [Fact]
    public async Task ListUserSubscriptions_NoCustomerYet_ReturnsEmpty()
    {
        var handler = new StubHandler();
        handler.Enqueue(HttpStatusCode.NotFound, "{}");
        var service = CreateService(handler);

        var subscriptions = await service.ListUserSubscriptionsAsync("user1", default);

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task ListUserSubscriptions_ReturnsMappedSubscriptions()
    {
        var handler = new StubHandler();
        handler.EnqueueJson(HttpStatusCode.OK, CustomerJson);
        handler.EnqueueJson(HttpStatusCode.OK,
            $"[ {SubscriptionJson} ]");
        var service = CreateService(handler);

        var subscriptions = await service.ListUserSubscriptionsAsync("user1", default);

        var subscription = Assert.Single(subscriptions);
        Assert.Equal(900, subscription.Id);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal("active", subscription.State);
        Assert.Equal(299m, subscription.Price);
    }
}
