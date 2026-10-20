using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Subscriptions;

public class MaxioSubscriptionServiceTests
{
    private const string Products =
        "[{\"product\":{\"handle\":\"eshop-pro\",\"name\":\"Pro Plan\",\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\"}}]";
    private const string Subscription =
        "{\"subscription\":{\"id\":22,\"state\":\"active\",\"product_price_in_cents\":29900,\"reference\":\"user-1\"," +
        "\"next_assessment_at\":\"2026-11-20T12:00:20+05:00\",\"product\":{\"handle\":\"eshop-pro\",\"name\":\"Pro Plan\"," +
        "\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\"}}}";

    private sealed class FakeMaxio : HttpMessageHandler
    {
        public List<string> Calls { get; } = new();
        public bool FailSubscriptionPostAfterSend { get; set; }

        public int Count(string call) => Calls.Count(c => c == call);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Calls.Add(request.Method + " " + path);
            HttpResponseMessage Json(HttpStatusCode code, string body) =>
                new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json"), RequestMessage = request };

            if (path.EndsWith("/products.json")) return Task.FromResult(Json(HttpStatusCode.OK, Products));
            if (path.EndsWith("/customers/lookup.json")) return Task.FromResult(Json(HttpStatusCode.NotFound, "{}"));
            if (request.Method == HttpMethod.Post && path.EndsWith("/customers.json"))
                return Task.FromResult(Json(HttpStatusCode.Created, "{\"customer\":{\"id\":11,\"email\":\"a@b.c\",\"reference\":\"user-1\"}}"));
            if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
            {
                if (FailSubscriptionPostAfterSend) throw new HttpRequestException("connection reset");
                return Task.FromResult(Json(HttpStatusCode.Created, Subscription));
            }
            if (path.EndsWith("/customers/11/subscriptions.json")) return Task.FromResult(Json(HttpStatusCode.OK, "[" + Subscription + "]"));
            if (path.EndsWith("/subscriptions/22.json")) return Task.FromResult(Json(HttpStatusCode.OK, Subscription));
            return Task.FromResult(Json(HttpStatusCode.NotFound, "{}"));
        }
    }

    private static (MaxioSubscriptionService Service, FakeMaxio Maxio) Create()
    {
        var maxio = new FakeMaxio();
        var options = new MaxioAdvancedBillingClientOptions();
        options.Server.Production.Us.BaseUrl = "https://maxio.test";
        var client = new MaxioAdvancedBillingClient(new HttpClient(maxio), options);
        var db = new CatalogContext(new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var settings = Options.Create(new MaxioSettings { ApiKey = "k", Subdomain = "s", ProductFamilyHandle = "fam" });
        return (new MaxioSubscriptionService(client, db, settings, NullLogger<MaxioSubscriptionService>.Instance), maxio);
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerAndSubscriptionOnce_AndRepeatIsIdempotent()
    {
        var (service, maxio) = Create();

        var first = await service.SubscribeAsync("user-1", "a@b.c", "A B", "eshop-pro", default);
        var second = await service.SubscribeAsync("user-1", "a@b.c", "A B", "eshop-pro", default);

        Assert.False(first.AlreadySubscribed);
        Assert.Equal("active", first.Subscription.State);
        Assert.Equal(29900, first.Subscription.PriceInCents);
        Assert.NotNull(first.Subscription.NextBillingAt);
        Assert.True(second.AlreadySubscribed);
        Assert.Equal(1, maxio.Count("POST /customers.json"));
        Assert.Equal(1, maxio.Count("POST /subscriptions.json"));
    }

    [Fact]
    public async Task Subscribe_RejectsPlanNotOfferedByTheFamily_WithoutWriting()
    {
        var (service, maxio) = Create();

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() =>
            service.SubscribeAsync("user-1", "a@b.c", "A B", "not-a-plan", default));

        Assert.Equal(0, maxio.Calls.Count(c => c.StartsWith("POST")));
    }

    [Fact]
    public async Task Subscribe_ConnectionFailsAfterWrite_SettlesFromMaxioStateWithoutResending()
    {
        var (service, maxio) = Create();
        maxio.FailSubscriptionPostAfterSend = true;

        var result = await service.SubscribeAsync("user-1", "a@b.c", "A B", "eshop-pro", default);

        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(1, maxio.Count("POST /subscriptions.json"));
        Assert.Equal(1, maxio.Count("GET /customers/11/subscriptions.json"));
        var mine = await service.GetSubscriptionsAsync("user-1", default);
        Assert.Single(mine);
    }

    [Fact]
    public async Task ListPlans_ReturnsPlansOfTheFamily()
    {
        var (service, _) = Create();

        var plans = await service.ListPlansAsync(default);

        var plan = Assert.Single(plans.Plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.False(plans.IsTruncated);
    }
}
