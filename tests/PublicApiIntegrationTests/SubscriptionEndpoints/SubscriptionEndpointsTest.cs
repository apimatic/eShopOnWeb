using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public sealed class SubscriptionEndpointsTest : IDisposable
{
    private readonly FakeMaxioBillingGateway _gateway = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public SubscriptionEndpointsTest()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMaxioBillingGateway>();
                services.AddSingleton<IMaxioBillingGateway>(_gateway);
            });
        });
        _client = _factory.CreateClient();
    }

    [TestMethod]
    public async Task AllRoutesRequireBearerToken()
    {
        var plans = await _client.GetAsync("api/subscription-plans");
        var create = await _client.PostAsJsonAsync("api/subscriptions", new { productHandle = "eshop-pro" });
        var subscriptions = await _client.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, plans.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, create.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, subscriptions.StatusCode);
    }

    [TestMethod]
    public async Task ListsConfiguredPlansForAuthenticatedShopper()
    {
        Authenticate();

        var response = await _client.GetAsync("api/subscription-plans");
        var plans = await response.Content.ReadFromJsonAsync<List<SubscriptionPlanDto>>();

        response.EnsureSuccessStatusCode();
        Assert.IsNotNull(plans);
        CollectionAssert.AreEquivalent(new[] { "basic-plan", "eshop-pro" },
            plans.Select(plan => plan.Handle).ToArray());
    }

    [TestMethod]
    public async Task ConcurrentReplayCreatesOneSubscriptionAndListsProviderState()
    {
        Authenticate();
        var idempotencyKey = Guid.NewGuid().ToString("N");

        var first = CreateRequest("eshop-pro", idempotencyKey);
        var second = CreateRequest("eshop-pro", idempotencyKey);
        var responses = await Task.WhenAll(_client.SendAsync(first), _client.SendAsync(second));
        var bodies = await Task.WhenAll(responses.Select(response =>
            response.Content.ReadFromJsonAsync<SubscriptionDto>()));

        Assert.IsTrue(responses.All(response => response.IsSuccessStatusCode));
        Assert.AreEqual(1, _gateway.CreateCount);
        Assert.IsNotNull(bodies[0]);
        Assert.IsNotNull(bodies[1]);
        Assert.AreEqual(bodies[0]!.Id, bodies[1]!.Id);

        var listResponse = await _client.GetAsync("api/my-subscriptions");
        var subscriptions = await listResponse.Content.ReadFromJsonAsync<List<SubscriptionDto>>();

        listResponse.EnsureSuccessStatusCode();
        Assert.IsNotNull(subscriptions);
        Assert.IsTrue(subscriptions.Any(subscription =>
            subscription.Id == bodies[0]!.Id &&
            subscription.ProductHandle == "eshop-pro" &&
            subscription.State == "active" &&
            subscription.NextBillingDate.HasValue));
    }

    [TestMethod]
    public async Task ReusingKeyForDifferentPlanReturnsConflictWithoutSecondCreate()
    {
        Authenticate();
        var idempotencyKey = Guid.NewGuid().ToString("N");

        var first = await _client.SendAsync(CreateRequest("eshop-pro", idempotencyKey));
        var second = await _client.SendAsync(CreateRequest("basic-plan", idempotencyKey));

        first.EnsureSuccessStatusCode();
        Assert.AreEqual(HttpStatusCode.Conflict, second.StatusCode);
        Assert.AreEqual(1, _gateway.CreateCount);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private void Authenticate() =>
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

    private static HttpRequestMessage CreateRequest(string productHandle, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "api/subscriptions")
        {
            Content = JsonContent.Create(new { productHandle })
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    private sealed class FakeMaxioBillingGateway : IMaxioBillingGateway
    {
        private readonly ConcurrentDictionary<string, SubscriptionDto> _subscriptions = new();
        private int _createCount;

        public int CreateCount => _createCount;

        public Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubscriptionPlanDto>>(new[]
            {
                new SubscriptionPlanDto("basic-plan", "Basic", 2900, 1, "month"),
                new SubscriptionPlanDto("eshop-pro", "Pro", 29900, 1, "month")
            });

        public async Task<SubscriptionPlanDto> GetPlanAsync(
            string productHandle,
            CancellationToken cancellationToken) =>
            (await GetPlansAsync(cancellationToken)).Single(plan => plan.Handle == productHandle);

        public Task<BillingCustomer> EnsureCustomerAsync(
            BillingCustomerProfile profile,
            CancellationToken cancellationToken) =>
            Task.FromResult(new BillingCustomer(101, $"eshop-user:{profile.StableUserId}"));

        public Task<SubscriptionDto?> FindSubscriptionAsync(
            string reference,
            CancellationToken cancellationToken) =>
            Task.FromResult(_subscriptions.TryGetValue(reference, out var subscription) ? subscription : null);

        public async Task<SubscriptionDto> CreateSubscriptionAsync(
            string productHandle,
            string customerReference,
            string subscriptionReference,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _createCount);
            await Task.Delay(100, cancellationToken);
            var plan = await GetPlanAsync(productHandle, cancellationToken);
            var subscription = new SubscriptionDto(
                9001,
                plan.Handle,
                plan.Name,
                plan.PriceInCents,
                "active",
                DateTimeOffset.UtcNow.AddMonths(1));
            _subscriptions[subscriptionReference] = subscription;
            return subscription;
        }

        public Task<IReadOnlyList<SubscriptionDto>> GetCustomerSubscriptionsAsync(
            string customerReference,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubscriptionDto>>(_subscriptions.Values.ToArray());
    }
}
