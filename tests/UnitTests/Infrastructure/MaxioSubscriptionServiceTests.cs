using System.Net;
using System.Text;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure;

public class MaxioSubscriptionServiceTests
{
    private const string ProductsJson = """
        [{"product":{"handle":"basic-plan","name":"Basic Plan","price_in_cents":2900,"interval":1,"interval_unit":"month"}},
         {"product":{"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]
        """;
    private const string CustomerJson = """{"customer":{"id":5,"reference":"u1","email":"a@b.c"}}""";
    private const string SubscriptionJson = """
        {"subscription":{"id":9,"state":"active","reference":"eshop:u1:basic-plan","product_price_in_cents":2900,
         "current_period_ends_at":"2030-01-01T00:00:00Z",
         "product":{"handle":"basic-plan","name":"Basic Plan","price_in_cents":2900}}}
        """;

    private sealed class FakeMaxio : HttpMessageHandler
    {
        public List<string> Calls { get; } = new();
        public bool CustomerExists { get; set; }
        public bool SubscriptionExists { get; set; }
        public Func<HttpRequestMessage, HttpResponseMessage?>? Override { get; set; }

        public int Count(HttpMethod method, string pathPart) =>
            Calls.Count(c => c.StartsWith(method.Method + " ") && c.Contains(pathPart));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.PathAndQuery;
            Calls.Add($"{request.Method} {path}");
            var custom = Override?.Invoke(request);
            if (custom is not null) return Task.FromResult(custom);

            HttpResponseMessage Json(HttpStatusCode code, string body) =>
                new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json"), RequestMessage = request };

            if (request.Method == HttpMethod.Get && path.Contains("/products"))
                return Task.FromResult(Json(HttpStatusCode.OK, ProductsJson));
            if (request.Method == HttpMethod.Get && path.Contains("/customers/lookup"))
                return Task.FromResult(CustomerExists ? Json(HttpStatusCode.OK, CustomerJson) : Json(HttpStatusCode.NotFound, "{}"));
            if (request.Method == HttpMethod.Post && path.Contains("/customers"))
            {
                CustomerExists = true;
                return Task.FromResult(Json(HttpStatusCode.Created, CustomerJson));
            }
            if (request.Method == HttpMethod.Get && path.Contains("/subscriptions"))
                return Task.FromResult(Json(HttpStatusCode.OK, SubscriptionExists ? "[" + SubscriptionJson + "]" : "[]"));
            if (request.Method == HttpMethod.Post && path.Contains("/subscriptions"))
            {
                SubscriptionExists = true;
                return Task.FromResult(Json(HttpStatusCode.Created, SubscriptionJson));
            }
            return Task.FromResult(Json(HttpStatusCode.NotFound, "{}"));
        }
    }

    private static (MaxioSubscriptionService Service, CatalogContext Db, FakeMaxio Maxio) Create(string? dbName = null, FakeMaxio? sharedMaxio = null)
    {
        var maxio = sharedMaxio ?? new FakeMaxio();
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "k", Password = "x" },
            // never retry in tests: a stubbed 5xx on GET would otherwise sleep for the backoff
            Retry = MaxioAdvancedBilling.Core.Configuration.RetryOptions.Disabled(),
        };
        options.Server.Production.Us.BaseUrl = "https://maxio.test";
        var client = new MaxioAdvancedBillingClient(new HttpClient(maxio), options);

        var db = new CatalogContext(new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString()).Options);
        var settings = Options.Create(new MaxioSettings { ApiKey = "k", Subdomain = "s", ProductFamilyHandle = "fam" });
        return (new MaxioSubscriptionService(client, db, settings, NullLogger<MaxioSubscriptionService>.Instance), db, maxio);
    }

    [Fact]
    public async Task ListPlans_ReturnsOfferedPlansFromConfiguredFamily()
    {
        var (service, _, maxio) = Create();

        var plans = await service.ListPlansAsync();

        Assert.Equal(new[] { "basic-plan", "eshop-pro" }, plans.Plans.Select(p => p.Handle));
        Assert.False(plans.IsTruncated);
        Assert.Contains("handle%3Afam", maxio.Calls[0]);
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerAndSubscriptionOnce_AndRepeatIsIdempotent()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, db, maxio) = Create(dbName);
        // a repeat is a new request, hence a new scoped service and DbContext over the same store
        var (service2, _, _) = Create(dbName, maxio);

        var first = await service.SubscribeAsync("u1", "user", "a@b.c", "basic-plan");
        var second = await service2.SubscribeAsync("u1", "user", "a@b.c", "basic-plan");

        Assert.False(first.AlreadySubscribed);
        Assert.Equal("active", first.Subscription.State);
        Assert.Equal(2900, first.Subscription.PriceInCents);
        Assert.NotNull(first.Subscription.NextBillingAt);
        Assert.True(second.AlreadySubscribed);
        Assert.Equal(1, maxio.Count(HttpMethod.Post, "/customers"));
        Assert.Equal(1, maxio.Count(HttpMethod.Post, "/subscriptions"));
        Assert.Equal(5, (await db.MaxioCustomerLinks.SingleAsync()).MaxioCustomerId);
        Assert.Equal(ClaimStatus.Complete, (await db.UserSubscriptions.SingleAsync()).Status);
    }

    [Fact]
    public async Task Subscribe_RejectsPlanNotOffered_BeforeAnyWrite()
    {
        var (service, db, maxio) = Create();

        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => service.SubscribeAsync("u1", "user", null, "not-a-plan"));

        Assert.Equal(SubscriptionFailure.PlanNotOffered, ex.Failure);
        Assert.Equal(0, maxio.Count(HttpMethod.Post, ""));
        Assert.Empty(db.MaxioCustomerLinks);
    }

    [Fact]
    public async Task Subscribe_WhileAnotherRequestHoldsTheClaim_IsRejectedWithoutReachingMaxio()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, _, maxio) = Create(dbName);
        var (_, other, _) = Create(dbName); // another request's context holding the claim
        other.MaxioCustomerLinks.Add(new MaxioCustomerLink { UserId = "u1", Status = ClaimStatus.Pending, ClaimedAt = DateTimeOffset.UtcNow });
        await other.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => service.SubscribeAsync("u1", "user", null, "basic-plan"));

        Assert.Equal(SubscriptionFailure.InProgress, ex.Failure);
        Assert.Equal(0, maxio.Count(HttpMethod.Post, ""));
    }

    [Fact]
    public async Task Subscribe_ReusesExistingMaxioCustomer_WhenLocalLinkIsMissing()
    {
        var (service, _, maxio) = Create();
        maxio.CustomerExists = true;

        await service.SubscribeAsync("u1", "user", "a@b.c", "basic-plan");

        Assert.Equal(0, maxio.Count(HttpMethod.Post, "/customers"));
    }

    [Fact]
    public async Task Subscribe_ProviderRejection_ReleasesClaimSoRetryIsPossible()
    {
        var (service, db, maxio) = Create();
        maxio.Override = r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("/subscriptions")
            ? new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            {
                Content = new StringContent("""{"errors":["no payment method"]}""", Encoding.UTF8, "application/json"),
                RequestMessage = r,
            }
            : null;

        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => service.SubscribeAsync("u1", "user", null, "basic-plan"));

        Assert.Equal(SubscriptionFailure.Rejected, ex.Failure);
        Assert.Empty(db.UserSubscriptions);
    }

    [Fact]
    public async Task Subscribe_ConnectionFailureAfterSend_SettlesByReReadingMaxio_WithoutSecondPost()
    {
        var (service, db, maxio) = Create();
        maxio.Override = r =>
        {
            if (r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("/subscriptions"))
            {
                maxio.SubscriptionExists = true; // the provider acted, but we never saw the response
                throw new HttpRequestException("connection reset");
            }
            return null;
        };

        var result = await service.SubscribeAsync("u1", "user", "a@b.c", "basic-plan");

        Assert.Equal(9, result.Subscription.SubscriptionId);
        Assert.Equal(1, maxio.Count(HttpMethod.Post, "/subscriptions"));
        Assert.Equal(ClaimStatus.Complete, (await db.UserSubscriptions.SingleAsync()).Status);
    }

    [Fact]
    public async Task Subscribe_ConnectionFailureThatCannotBeSettled_IsRecordedAsUnknown()
    {
        var (service, db, maxio) = Create();
        maxio.Override = r =>
            r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("/subscriptions")
                ? throw new HttpRequestException("connection reset")
                : null;

        var ex = await Assert.ThrowsAsync<SubscriptionException>(() => service.SubscribeAsync("u1", "user", null, "basic-plan"));

        Assert.Equal(SubscriptionFailure.OutcomeUnknown, ex.Failure);
        Assert.Equal(ClaimStatus.Unknown, (await db.UserSubscriptions.SingleAsync()).Status);
    }

    [Fact]
    public async Task MySubscriptions_WithoutLink_IsEmptyAndMakesNoCall()
    {
        var (service, _, maxio) = Create();

        var mine = await service.ListMySubscriptionsAsync("u1");

        Assert.Empty(mine);
        Assert.Empty(maxio.Calls);
    }

    [Fact]
    public void Settings_RequireKeysAndAbsoluteBaseUrl()
    {
        Assert.NotNull(new MaxioSettings { ProductFamilyHandle = "f", Subdomain = "s" }.Validate());
        Assert.NotNull(new MaxioSettings { ApiKey = "k", Subdomain = "s" }.Validate());
        Assert.NotNull(new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "f" }.Validate());
        Assert.NotNull(new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "f", BaseUrl = "not a url" }.Validate());
        Assert.Null(new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "f", BaseUrl = "https://x.test" }.Validate());
        Assert.Null(new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "f", Subdomain = "s" }.Validate());
    }
}
