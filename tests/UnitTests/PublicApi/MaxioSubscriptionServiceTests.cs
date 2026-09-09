using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi;

/// <summary>
/// Unit tests for the Maxio subscription integration. All Maxio traffic is served
/// by an in-memory fake, so no credentials or sandbox access are needed.
/// </summary>
public class MaxioSubscriptionServiceTests
{
    private static MaxioSettings BuildSettings() => new()
    {
        ApiKey = "test-key",
        Subdomain = "test-site",
        ProductFamilyHandle = "test-family"
    };

    private static ISubscriptionService BuildService(
        FakeMaxioHandler handler, out List<HttpRequestMessage> requests)
    {
        requests = handler.Requests;
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://test-site.chargify.com/") };
        var api = new MaxioApiClient(client, Options.Create(BuildSettings()));
        return new MaxioSubscriptionService(
            api, Options.Create(BuildSettings()), NullLogger<MaxioSubscriptionService>.Instance);
    }

    [Fact]
    public async Task GetPlansAsync_MapsFamilyProducts()
    {
        var handler = new FakeMaxioHandler();
        handler.EnqueueResponse(@"
{ ""items"": [ {
    ""product"": { ""id"": 1, ""handle"": ""pro"", ""name"": ""Pro"", ""price_in_cents"": 29900,
                   ""interval"": 1, ""interval_unit"": ""month"", ""require_credit_card"": false,
                   ""archived_at"": null }
  }, {
    ""product"": { ""id"": 2, ""handle"": ""old"", ""name"": ""Old"", ""price_in_cents"": 100,
                   ""interval"": 1, ""interval_unit"": ""month"",
                   ""archived_at"": ""2026-01-01T00:00:00Z"" }
  } ] }");

        var service = BuildService(handler, out var requests);
        var plans = await service.GetPlansAsync();

        Assert.Single(plans);
        Assert.Equal("pro", plans[0].Handle);
        Assert.Equal("Pro", plans[0].Name);
        Assert.Equal(299m, plans[0].Price);
        Assert.Contains(requests, r => r.RequestUri!.PathAndQuery.Contains("/product_families/handle:test-family/products.json"));
    }

    private const string ProPlanResponse = @"
{ ""items"": [ {
    ""product"": { ""id"": 1, ""handle"": ""pro"", ""name"": ""Pro"", ""price_in_cents"": 29900,
                   ""interval"": 1, ""interval_unit"": ""month"", ""require_credit_card"": false }
  } ] }";

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription_AndIsIdempotent()
    {
        var handler = new FakeMaxioHandler();
        // First signup: validate plan, lookup customer (404), create customer, check existing, create subscription.
        handler.EnqueueResponse(ProPlanResponse);                                // products.json
        handler.EnqueueResponse("{}");                                           // customer lookup -> 404 (status set below)
        handler.SetStatusForNext(HttpStatusCode.NotFound);
        handler.EnqueueResponse(@"{ ""customer"": { ""id"": 42, ""reference"": ""user-1"" } }"); // create customer
        handler.EnqueueResponse(@"{ ""items"": [] }");                           // existing subscriptions (none)
        handler.EnqueueResponse(@"{ ""subscription"": { ""id"": 100, ""state"": ""active"",
            ""customer"": { ""id"": 42 },
            ""product"": { ""handle"": ""pro"", ""name"": ""Pro"", ""price_in_cents"": 29900,
                           ""interval"": 1, ""interval_unit"": ""month"" },
            ""current_period_ends_at"": ""2026-10-09T00:00:00Z"" } }");          // create subscription
        // Replay of the same request: validate plan, lookup customer (exists), check existing.
        handler.EnqueueResponse(ProPlanResponse);                                // products.json
        handler.EnqueueResponse(@"{ ""customer"": { ""id"": 42, ""reference"": ""user-1"" } }"); // lookup
        handler.EnqueueResponse(@"{ ""items"": [ { ""subscription"": { ""id"": 100, ""state"": ""active"",
            ""customer"": { ""id"": 42 },
            ""product"": { ""handle"": ""pro"", ""name"": ""Pro"", ""price_in_cents"": 29900,
                           ""interval"": 1, ""interval_unit"": ""month"" } } } ] }"); // existing subscriptions

        var service = BuildService(handler, out var requests);
        var first = await service.SubscribeAsync("user-1", "user1@example.com", "pro");
        var second = await service.SubscribeAsync("user-1", "user1@example.com", "pro");

        Assert.Equal(100, first.Subscription.Id);
        Assert.Equal("active", first.Subscription.State);
        Assert.Equal(299m, first.Subscription.Price);
        Assert.False(first.AlreadyExisted);
        Assert.True(second.AlreadyExisted);
        Assert.Equal(100, second.Subscription.Id);

        // Exactly one customer create and one subscription create despite two signups.
        Assert.Equal(1, requests.Count(r => r.Method == HttpMethod.Post && r.RequestUri!.PathAndQuery.StartsWith("/customers.json")));
        Assert.Equal(1, requests.Count(r => r.Method == HttpMethod.Post && r.RequestUri!.PathAndQuery.StartsWith("/subscriptions.json")));
    }

    [Fact]
    public async Task SubscribeAsync_FallsBackToRemittance_WhenPaymentMethodRequired()
    {
        var handler = new FakeMaxioHandler();
        handler.EnqueueResponse(ProPlanResponse);                                 // products.json
        handler.EnqueueResponse(@"{ ""customer"": { ""id"": 42, ""reference"": ""user-1"" } }"); // lookup
        handler.EnqueueResponse(@"{ ""items"": [] }");                            // existing subscriptions
        handler.EnqueueResponse(@"{ ""errors"": [ ""No payment method was on file for the $299.00 balance"" ] }");
        handler.SetStatusForNext(HttpStatusCode.UnprocessableEntity);
        handler.EnqueueResponse(@"{ ""subscription"": { ""id"": 100, ""state"": ""active"",
            ""customer"": { ""id"": 42 },
            ""product"": { ""handle"": ""pro"", ""name"": ""Pro"", ""price_in_cents"": 29900,
                           ""interval"": 1, ""interval_unit"": ""month"" } } }"); // remittance retry

        var service = BuildService(handler, out var requests);
        var result = await service.SubscribeAsync("user-1", "user1@example.com", "pro");

        Assert.Equal(100, result.Subscription.Id);

        var creates = requests.Where(r => r.Method == HttpMethod.Post && r.RequestUri!.PathAndQuery.StartsWith("/subscriptions.json")).ToList();
        Assert.Equal(2, creates.Count);

        var retryBody = ReadJson(handler, creates[1]);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", retryBody);
    }

    [Fact]
    public async Task GetMySubscriptionsAsync_ReturnsEmpty_WhenNoCustomerYet()
    {
        var handler = new FakeMaxioHandler();
        handler.EnqueueResponse("{}");
        handler.SetStatusForNext(HttpStatusCode.NotFound);

        var service = BuildService(handler, out _);
        var subscriptions = await service.GetMySubscriptionsAsync("user-1");

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task SubscribeAsync_ThrowsForUnknownPlan()
    {
        var handler = new FakeMaxioHandler();
        handler.EnqueueResponse(@"{ ""items"": [] }");

        var service = BuildService(handler, out _);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SubscribeAsync("user-1", "user1@example.com", "does-not-exist"));
    }

    private static string ReadJson(FakeMaxioHandler handler, HttpRequestMessage request)
    {
        var index = handler.Requests.IndexOf(request);
        return handler.Bodies[index];
    }

    private class FakeMaxioHandler : HttpMessageHandler
    {
        private readonly List<(HttpStatusCode Status, string Body)> _responses = new();
        private int _cursor;

        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> Bodies { get; } = new();

        public void EnqueueResponse(string body) =>
            _responses.Add((HttpStatusCode.OK, body));

        /// <summary>Overrides the status of the most recently enqueued response.</summary>
        public void SetStatusForNext(HttpStatusCode status) =>
            _responses[^1] = (status, _responses[^1].Body);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null
                ? string.Empty
                : request.Content.ReadAsStringAsync().GetAwaiter().GetResult());

            var (status, body) = _cursor < _responses.Count ? _responses[_cursor++] : (HttpStatusCode.OK, "{}");

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
