using System.Net;
using System.Text;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionServiceTests
{
    private const string User = "shopper@example.test";

    private const string ProductsJson = """
        [{"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}},
         {"product":{"id":2,"name":"Old","handle":"old-plan","price_in_cents":100,"archived_at":"2024-01-01T00:00:00Z"}}]
        """;
    private const string CustomerJson = """{"customer":{"id":77,"reference":"shopper@example.test"}}""";
    private const string SubscriptionJson = """
        {"subscription":{"id":555,"state":"active","product_price_in_cents":29900,
          "next_assessment_at":"2030-01-01T00:00:00Z","product":{"handle":"eshop-pro","name":"Pro Plan"}}}
        """;

    private sealed class Stub : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string? Body)> Calls { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage>? Responder { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request.Method, request.RequestUri!.AbsolutePath,
                request.Content is null ? null : request.Content.ReadAsStringAsync().Result));
            var response = Responder!(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (MaxioSubscriptionService Service, Stub Stub, CatalogContext Db) Create(
        Func<HttpRequestMessage, HttpResponseMessage> responder, string? dbName = null)
    {
        var stub = new Stub { Responder = responder };
        var client = new MaxioAdvancedBillingClient(new HttpClient(stub), new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "key", Password = "x" },
            Retry = RetryOptions.Disabled()
        });
        var db = new CatalogContext(new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString()).Options);
        var settings = Options.Create(new MaxioSettings { ApiKey = "key", Subdomain = "site", ProductFamilyHandle = "fam" });
        return (new MaxioSubscriptionService(client, db, settings, NullLogger<MaxioSubscriptionService>.Instance), stub, db);
    }

    private static HttpResponseMessage Happy(HttpRequestMessage r, bool customerExists = false) => (r.Method.Method, r.RequestUri!.AbsolutePath) switch
    {
        ("GET", var p) when p.EndsWith("/products.json") => Json(HttpStatusCode.OK, ProductsJson),
        ("GET", var p) when p.EndsWith("/customers/lookup.json") =>
            customerExists ? Json(HttpStatusCode.OK, CustomerJson) : Json(HttpStatusCode.NotFound, "{}"),
        ("POST", var p) when p.EndsWith("/customers.json") => Json(HttpStatusCode.Created, CustomerJson),
        ("POST", var p) when p.EndsWith("/subscriptions.json") => Json(HttpStatusCode.Created, SubscriptionJson),
        _ => Json(HttpStatusCode.NotFound, "{}")
    };

    [Fact]
    public async Task ListPlans_ReturnsOnlyActivePlansWithPrice()
    {
        var (service, _, _) = Create(r => Happy(r));

        var plans = await service.ListPlansAsync();

        var plan = Assert.Single(plans.Plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.False(plans.IsPartial);
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerThenSubscription_AndSendsReferencesAndPlanHandle()
    {
        var (service, stub, db) = Create(r => Happy(r));

        var result = await service.SubscribeAsync(User, "eshop-pro");

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(555, result.Subscription.SubscriptionId);
        Assert.Equal("active", result.Subscription.State);
        Assert.NotNull(result.Subscription.NextBillingAt);

        var customerPost = Assert.Single(stub.Calls, c => c.Method == HttpMethod.Post && c.Path.EndsWith("/customers.json"));
        Assert.Contains("\"reference\":\"shopper@example.test\"", customerPost.Body);
        var subPost = Assert.Single(stub.Calls, c => c.Method == HttpMethod.Post && c.Path.EndsWith("/subscriptions.json"));
        Assert.Contains("\"product_handle\":\"eshop-pro\"", subPost.Body);
        Assert.Contains("\"customer_id\":77", subPost.Body);
        Assert.Contains("\"reference\":\"eshop:shopper@example.test:eshop-pro\"", subPost.Body);

        var claim = await db.SubscriptionClaims.SingleAsync();
        Assert.Equal(SubscriptionClaimStatus.Active, claim.Status);
        Assert.Equal(555, claim.MaxioSubscriptionId);
    }

    [Fact]
    public async Task Subscribe_UnknownPlan_FailsBeforeAnyMaxioWriteOrClaim()
    {
        var (service, stub, db) = Create(r => Happy(r));

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(User, "not-offered"));

        Assert.Equal(BillingFailure.PlanNotFound, ex.Failure);
        Assert.DoesNotContain(stub.Calls, c => c.Method == HttpMethod.Post);
        Assert.Empty(db.SubscriptionClaims);
    }

    [Fact]
    public async Task Subscribe_ArchivedPlanIsNotOffered()
    {
        var (service, _, _) = Create(r => Happy(r));

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(User, "old-plan"));

        Assert.Equal(BillingFailure.PlanNotFound, ex.Failure);
    }

    [Fact]
    public async Task Subscribe_Twice_SecondCallReturnsExistingSubscription_WithoutSecondWrite()
    {
        var dbName = Guid.NewGuid().ToString();
        var (first, firstStub, _) = Create(r => Happy(r), dbName);
        await first.SubscribeAsync(User, "eshop-pro");

        var (second, secondStub, _) = Create(r => r.RequestUri!.AbsolutePath.EndsWith("/subscriptions/lookup.json")
            ? Json(HttpStatusCode.OK, SubscriptionJson)
            : Happy(r, customerExists: true), dbName);
        var again = await second.SubscribeAsync(User, "eshop-pro");

        Assert.True(again.AlreadySubscribed);
        Assert.Equal(555, again.Subscription.SubscriptionId);
        Assert.DoesNotContain(secondStub.Calls, c => c.Method == HttpMethod.Post);
        Assert.Single(firstStub.Calls, c => c.Method == HttpMethod.Post && c.Path.EndsWith("/subscriptions.json"));
    }

    [Fact]
    public async Task Subscribe_ConcurrentClaimInFlight_IsRejectedWithoutReachingMaxio()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, stub, db) = Create(r => Happy(r), dbName);
        db.SubscriptionClaims.Add(new SubscriptionClaim(User, "eshop-pro", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(User, "eshop-pro"));

        Assert.Equal(BillingFailure.InProgress, ex.Failure);
        Assert.DoesNotContain(stub.Calls, c => c.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Subscribe_ProviderRejects_ReleasesTheClaimSoACorrectedRetryCanProceed()
    {
        var (service, _, db) = Create(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json")
            ? Json((HttpStatusCode)422, """{"errors":["No payment method was on file"]}""")
            : Happy(r));

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(User, "eshop-pro"));

        Assert.Equal(BillingFailure.Rejected, ex.Failure);
        Assert.Contains("No payment method", ex.Message);
        Assert.Empty(db.SubscriptionClaims);
    }

    [Fact]
    public async Task Subscribe_ConnectionFailsAfterWrite_ReconcilesByReference_AndReportsSuccess()
    {
        var posted = false;
        var (service, stub, db) = Create(r =>
        {
            if (r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json"))
            {
                posted = true;
                throw new HttpRequestException("connection reset");
            }
            if (r.RequestUri!.AbsolutePath.EndsWith("/subscriptions/lookup.json"))
                return posted ? Json(HttpStatusCode.OK, SubscriptionJson) : Json(HttpStatusCode.NotFound, "{}");
            return Happy(r);
        });

        var result = await service.SubscribeAsync(User, "eshop-pro");

        Assert.Equal(555, result.Subscription.SubscriptionId);
        Assert.Equal(SubscriptionClaimStatus.Active, (await db.SubscriptionClaims.SingleAsync()).Status);
        Assert.Single(stub.Calls, c => c.Method == HttpMethod.Post && c.Path.EndsWith("/subscriptions.json")); // never resent
    }

    [Fact]
    public async Task Subscribe_ConnectionFailsAfterWrite_AndNothingFound_RecordsUnknown_ThenNextSubscribeSettlesIt()
    {
        var dbName = Guid.NewGuid().ToString();
        var (first, _, db) = Create(r =>
        {
            if (r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json"))
                throw new HttpRequestException("connection reset");
            if (r.RequestUri!.AbsolutePath.EndsWith("/subscriptions/lookup.json"))
                return Json(HttpStatusCode.NotFound, "{}");
            return Happy(r);
        }, dbName);

        var ex = await Assert.ThrowsAsync<BillingException>(() => first.SubscribeAsync(User, "eshop-pro"));
        Assert.Equal(BillingFailure.OutcomeUnknown, ex.Failure);
        Assert.Equal(SubscriptionClaimStatus.Unknown, (await db.SubscriptionClaims.SingleAsync()).Status);

        // The next attempt finds nothing at Maxio for the reference, voids the stale claim, and subscribes.
        var (retry, retryStub, _) = Create(r =>
            r.RequestUri!.AbsolutePath.EndsWith("/subscriptions/lookup.json") ? Json(HttpStatusCode.NotFound, "{}") : Happy(r, customerExists: true), dbName);
        var result = await retry.SubscribeAsync(User, "eshop-pro");

        Assert.False(result.AlreadySubscribed);
        Assert.Single(retryStub.Calls, c => c.Method == HttpMethod.Post && c.Path.EndsWith("/subscriptions.json"));
    }

    [Fact]
    public async Task Subscribe_UnknownOutcomeThatActuallyLanded_IsAdoptedByTheNextSubscribe()
    {
        var dbName = Guid.NewGuid().ToString();
        var posted = false;
        var (first, _, _) = Create(r =>
        {
            if (r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json"))
            {
                posted = true;
                throw new HttpRequestException("connection reset");
            }
            if (r.RequestUri!.AbsolutePath.EndsWith("/subscriptions/lookup.json"))
            {
                if (posted) throw new HttpRequestException("still down");
                return Json(HttpStatusCode.NotFound, "{}");
            }
            return Happy(r);
        }, dbName);
        await Assert.ThrowsAsync<BillingException>(() => first.SubscribeAsync(User, "eshop-pro"));

        var (retry, retryStub, db) = Create(r =>
            r.RequestUri!.AbsolutePath.EndsWith("/subscriptions/lookup.json") ? Json(HttpStatusCode.OK, SubscriptionJson) : Happy(r, customerExists: true), dbName);
        var result = await retry.SubscribeAsync(User, "eshop-pro");

        Assert.True(result.AlreadySubscribed);
        Assert.DoesNotContain(retryStub.Calls, c => c.Method == HttpMethod.Post);
        Assert.Equal(SubscriptionClaimStatus.Active, (await db.SubscriptionClaims.SingleAsync()).Status);
    }

    [Fact]
    public async Task Subscribe_CustomerCreateConnectionFails_ButCustomerExists_UsesThatCustomer()
    {
        var lookups = 0;
        var (service, _, _) = Create(r =>
        {
            if (r.RequestUri!.AbsolutePath.EndsWith("/customers/lookup.json"))
                return ++lookups == 1 ? Json(HttpStatusCode.NotFound, "{}") : Json(HttpStatusCode.OK, CustomerJson);
            if (r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/customers.json"))
                throw new HttpRequestException("connection reset");
            return Happy(r);
        });

        var result = await service.SubscribeAsync(User, "eshop-pro");

        Assert.Equal(555, result.Subscription.SubscriptionId);
    }

    [Fact]
    public async Task Subscribe_MalformedSuccessBody_IsAnUnknownOutcomeNotACrash()
    {
        var (service, _, _) = Create(r =>
            r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json")
                ? Json(HttpStatusCode.Created, """{"subscription":"oops"}""")
                : r.RequestUri!.AbsolutePath.EndsWith("/subscriptions/lookup.json") ? Json(HttpStatusCode.NotFound, "{}") : Happy(r));

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(User, "eshop-pro"));

        Assert.Equal(BillingFailure.OutcomeUnknown, ex.Failure);
    }

    [Fact]
    public async Task ProviderAuthFailure_IsReportedAsProviderUnavailable_NotAsCallerError()
    {
        var (service, _, _) = Create(_ => Json(HttpStatusCode.Unauthorized, "{}"));

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.ListPlansAsync());

        Assert.Equal(BillingFailure.ProviderUnavailable, ex.Failure);
    }

    [Fact]
    public async Task Subscribe_MaxioAlreadyHoldsTheSubscription_ButNoLocalClaim_AdoptsItWithoutWriting()
    {
        var (service, stub, db) = Create(r =>
            r.RequestUri!.AbsolutePath.EndsWith("/subscriptions/lookup.json") ? Json(HttpStatusCode.OK, SubscriptionJson) : Happy(r));

        var result = await service.SubscribeAsync(User, "eshop-pro");

        Assert.True(result.AlreadySubscribed);
        Assert.DoesNotContain(stub.Calls, c => c.Method == HttpMethod.Post);
        Assert.Equal(SubscriptionClaimStatus.Active, (await db.SubscriptionClaims.SingleAsync()).Status);
    }

    [Fact]
    public async Task ListSubscriptions_NoCustomer_IsEmpty()
    {
        var (service, _, _) = Create(r => Happy(r));

        Assert.Empty(await service.ListSubscriptionsAsync(User));
    }

    [Fact]
    public async Task ListSubscriptions_ReadsFromMaxioByCustomerReference()
    {
        var (service, stub, _) = Create(r => r.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json") && r.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, "[" + SubscriptionJson + "]")
            : Happy(r, customerExists: true));

        var subs = await service.ListSubscriptionsAsync(User);

        Assert.Equal(555, Assert.Single(subs).SubscriptionId);
        Assert.Contains(stub.Calls, c => c.Path.Contains("/customers/77/subscriptions"));
    }

    [Theory]
    [InlineData(null, "s", "p")]
    [InlineData("k", " ", "p")]
    [InlineData("k", "s", "")]
    public void SettingsValidator_RejectsMissingOrBlankRequiredSettings(string? key, string? subdomain, string? family)
    {
        var result = new MaxioSettingsValidator().Validate(null,
            new MaxioSettings { ApiKey = key, Subdomain = subdomain, ProductFamilyHandle = family });

        Assert.True(result.Failed);
        Assert.All(result.Failures!, f => Assert.Contains("Maxio:", f));
    }

    [Fact]
    public void SettingsValidator_AcceptsCompleteSettings_BaseUrlOptional()
    {
        var result = new MaxioSettingsValidator().Validate(null,
            new MaxioSettings { ApiKey = "k", Subdomain = "s", ProductFamilyHandle = "p" });

        Assert.True(result.Succeeded);
    }
}
