using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioBillingServiceTests
{
    private const string UserEmail = "user@example.com";

    private static MaxioOptions DefaultOptions => new MaxioOptions
    {
        ApiKey = "test-key",
        Subdomain = "test",
        ProductFamilyHandle = "eshop-subscribe"
    };

    private static MaxioBillingService CreateService(FakeMaxioHandler handler, MaxioOptions? options = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test.chargify.com/") };
        var factory = new StubHttpClientFactory(httpClient);
        return new MaxioBillingService(
            factory,
            Options.Create(options ?? DefaultOptions),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<MaxioBillingService>.Instance);
    }

    private const string FamilyLookupPath = "/product_families/lookup.json?handle=eshop-subscribe";
    private const string ProductsPath = "/product_families/10/products.json";
    private const string CustomerLookupPath = "/customers/lookup.json?reference=user%40example.com";

    private const string FamilyJson =
        @"{""product_family"":{""id"":10,""handle"":""eshop-subscribe"",""name"":""eShopSubscribe""}}";

    private const string ProductsJson =
        @"[{""product"":{""id"":713,""handle"":""eshop-pro"",""name"":""Pro Plan"",""description"":""Pro"",""price_in_cents"":29900,""interval"":1,""interval_unit"":""month"",""archived_at"":null}}," +
        @"{""product"":{""id"":714,""handle"":""old-plan"",""name"":""Old"",""price_in_cents"":1000,""interval"":1,""interval_unit"":""month"",""archived_at"":""2026-01-01T00:00:00-05:00""}}]";

    private const string CustomerJson =
        @"{""customer"":{""id"":55,""reference"":""user@example.com"",""email"":""user@example.com"",""first_name"":""user"",""last_name"":""Customer""}}";

    private const string SubscriptionJson =
        @"{""subscription"":{""id"":77,""state"":""active"",""reference"":""eshop-sub:user@example.com:eshop-pro"",""next_assessment_at"":""2026-11-06T00:00:00-05:00"",""current_period_ends_at"":""2026-11-06T00:00:00-05:00"",""created_at"":""2026-10-06T00:00:00-05:00"",""product"":{""id"":713,""handle"":""eshop-pro"",""name"":""Pro Plan"",""price_in_cents"":29900,""interval"":1,""interval_unit"":""month"",""archived_at"":null}}}";

    private const string CondensedSubscriptionJson =
        @"[{""subscription"":{""id"":77,""state"":""active"",""reference"":""eshop-sub:user@example.com:eshop-pro"",""next_assessment_at"":""2026-11-06T00:00:00-05:00"",""current_period_ends_at"":""2026-11-06T00:00:00-05:00"",""created_at"":""2026-10-06T00:00:00-05:00""}}]";

    private static void AddDefaultPlanRoutes(FakeMaxioHandler handler)
    {
        handler.On(HttpMethod.Get, FamilyLookupPath, HttpStatusCode.OK, FamilyJson);
        handler.On(HttpMethod.Get, ProductsPath, HttpStatusCode.OK, ProductsJson);
    }

    [Fact]
    public async Task GetAvailablePlansAsync_ExcludesArchivedProducts_AndMapsFields()
    {
        var handler = new FakeMaxioHandler();
        AddDefaultPlanRoutes(handler);
        var service = CreateService(handler);

        var plans = await service.GetAvailablePlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(299m, plan.Price);
        Assert.Equal(1, plan.Interval);
        Assert.Equal("month", plan.IntervalUnit);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription_WhenNoneExist()
    {
        var handler = new FakeMaxioHandler();
        AddDefaultPlanRoutes(handler);
        handler.On(HttpMethod.Get, CustomerLookupPath, HttpStatusCode.NotFound, @"{""errors"":[""not found""]}");
        handler.On(HttpMethod.Post, "/customers.json", HttpStatusCode.Created, CustomerJson);
        handler.On(HttpMethod.Get, $"/subscriptions.json?customer_id=55&page=1&per_page=100", HttpStatusCode.OK, @"[]");
        handler.On(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created, SubscriptionJson);
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(UserEmail, "eshop-pro");

        Assert.Equal(77, result.SubscriptionId);
        Assert.Equal("eshop-pro", result.PlanHandle);
        Assert.Equal("Pro Plan", result.PlanName);
        Assert.Equal(299m, result.Price);
        Assert.Equal("active", result.State);
        // 2026-11-06T00:00:00-05:00 — compare the date only, to stay timezone-agnostic.
        Assert.Equal(new DateTime(2026, 11, 6), result.NextBillingDate!.Value.Date);

        var createBody = Assert.Single(handler.Bodies.Where(b => b.Request.Method == HttpMethod.Post && b.Request.RequestUri!.AbsolutePath == "/subscriptions.json")).Body;
        Assert.Contains("\"payment_collection_method\":\"invoice\"", createBody);
        Assert.Contains("\"reference\":\"eshop-sub:user@example.com:eshop-pro\"", createBody);
        Assert.Contains("\"customer_id\":55", createBody);
    }

    [Fact]
    public async Task SubscribeAsync_ReusesExistingSubscription_WithoutCreatingDuplicates()
    {
        var handler = new FakeMaxioHandler();
        AddDefaultPlanRoutes(handler);
        handler.On(HttpMethod.Get, CustomerLookupPath, HttpStatusCode.OK, CustomerJson);
        handler.On(HttpMethod.Get, $"/subscriptions.json?customer_id=55&page=1&per_page=100", HttpStatusCode.OK, CondensedSubscriptionJson);
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(UserEmail, "eshop-pro");

        Assert.Equal(77, result.SubscriptionId);
        Assert.Equal("eshop-pro", result.PlanHandle);
        Assert.DoesNotContain(handler.Bodies, b => b.Request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task SubscribeAsync_ThrowsNotFound_WhenPlanDoesNotExist()
    {
        var handler = new FakeMaxioHandler();
        AddDefaultPlanRoutes(handler);
        var service = CreateService(handler);

        var exception = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(UserEmail, "no-such-plan"));

        Assert.Equal(404, exception.StatusCode);
    }

    [Fact]
    public async Task SubscribeAsync_Throws_WhenUserEmailIsInvalid()
    {
        var handler = new FakeMaxioHandler();
        var service = CreateService(handler);

        await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync("not-an-email", "eshop-pro"));
    }

    [Fact]
    public async Task GetSubscriptionsForUserAsync_ReturnsEmpty_WhenCustomerDoesNotExist()
    {
        var handler = new FakeMaxioHandler();
        handler.On(HttpMethod.Get, CustomerLookupPath, HttpStatusCode.NotFound, @"{""errors"":[""not found""]}");
        var service = CreateService(handler);

        var result = await service.GetSubscriptionsForUserAsync(UserEmail);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSubscriptionsForUserAsync_ExpandsCondensedSubscriptions_WithProductDetails()
    {
        var handler = new FakeMaxioHandler();
        handler.On(HttpMethod.Get, CustomerLookupPath, HttpStatusCode.OK, CustomerJson);
        handler.On(HttpMethod.Get, $"/subscriptions.json?customer_id=55&page=1&per_page=100", HttpStatusCode.OK, CondensedSubscriptionJson);
        handler.On(HttpMethod.Get, "/subscriptions/77.json", HttpStatusCode.OK, SubscriptionJson);
        var service = CreateService(handler);

        var result = await service.GetSubscriptionsForUserAsync(UserEmail);

        var subscription = Assert.Single(result);
        Assert.Equal(77, subscription.SubscriptionId);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal("Pro Plan", subscription.PlanName);
        Assert.Equal(299m, subscription.Price);
        Assert.Equal("active", subscription.State);
    }

    [Fact]
    public async Task SubscribeAsync_ThrowsBillingException_WithMaxioErrorMessage_OnValidationFailure()
    {
        var handler = new FakeMaxioHandler();
        AddDefaultPlanRoutes(handler);
        handler.On(HttpMethod.Get, CustomerLookupPath, HttpStatusCode.OK, CustomerJson);
        handler.On(HttpMethod.Get, $"/subscriptions.json?customer_id=55&page=1&per_page=100", HttpStatusCode.OK, @"[]");
        handler.On(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.UnprocessableEntity,
            @"{""errors"":[""No payment method was on file for the $299.00 balance""]}");
        var service = CreateService(handler);

        var exception = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(UserEmail, "eshop-pro"));

        Assert.Contains("No payment method was on file", exception.Message);
    }

    private class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _httpClient;

        public StubHttpClientFactory(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public HttpClient CreateClient(string name) => _httpClient;
    }

    private class FakeMaxioHandler : HttpMessageHandler
    {
        private readonly List<(HttpMethod Method, string PathAndQuery, HttpStatusCode Status, string Body)> _routes =
            new List<(HttpMethod, string, HttpStatusCode, string)>();

        public List<(HttpRequestMessage Request, string? Body)> Bodies { get; } =
            new List<(HttpRequestMessage, string?)>();

        public void On(HttpMethod method, string pathAndQuery, HttpStatusCode status, string body) =>
            _routes.Add((method, pathAndQuery, status, body));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Bodies.Add((request, body));

            var route = _routes.FirstOrDefault(r =>
                r.Method == request.Method &&
                string.Equals(r.PathAndQuery, request.RequestUri!.PathAndQuery, StringComparison.Ordinal));

            var response = new HttpResponseMessage(route.Status);
            response.Content = new StringContent(route.Body ?? @"{""errors"":[""unexpected request""]}", Encoding.UTF8, "application/json");
            return response;
        }
    }
}