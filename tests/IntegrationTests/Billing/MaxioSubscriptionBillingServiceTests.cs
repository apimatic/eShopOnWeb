#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private const string ProductsPath = "/product_families/handle:test-family/products.json";
    private const string CustomerLookupPath = "/customers/lookup.json";
    private const string CustomersPath = "/customers.json";
    private const string CustomerSubscriptionsPath = "/customers/42/subscriptions.json";
    private const string SubscriptionsPath = "/subscriptions.json";
    private const string SubscriptionLookupPath = "/subscriptions/lookup.json";

    private static readonly Subscriber Shopper = new("demouser@microsoft.com", "demouser@microsoft.com");

    private readonly string _databaseName = $"maxio-{Guid.NewGuid():N}";
    private readonly MaxioStub _maxio = new();
    private readonly MaxioSettings _settings = new()
    {
        ApiKey = "test-key",
        ProductFamilyHandle = "test-family",
        BaseUrl = MaxioStub.BaseUrl
    };

    public MaxioSubscriptionBillingServiceTests()
    {
        _maxio.On(HttpMethod.Get, ProductsPath, (_, _) => MaxioStub.Json(HttpStatusCode.OK,
            $"[{MaxioStub.Product("pro", "Pro Plan", 29900)},{MaxioStub.Product("basic", "Basic Plan", 2900)},{MaxioStub.Product("old", "Old Plan", 100, archivedAt: "2025-01-01T00:00:00Z")}]"));
        _maxio.On(HttpMethod.Get, CustomerLookupPath, (_, _) => MaxioStub.Json(HttpStatusCode.NotFound, ""));
        _maxio.On(HttpMethod.Post, CustomersPath, (_, _) => MaxioStub.Json(HttpStatusCode.Created,
            """{"customer":{"id":42,"reference":"demouser@microsoft.com","email":"demouser@microsoft.com"}}"""));
        _maxio.On(HttpMethod.Get, CustomerSubscriptionsPath, (_, _) => MaxioStub.Json(HttpStatusCode.OK, "[]"));
        _maxio.On(HttpMethod.Post, SubscriptionsPath, (_, body) => MaxioStub.Json(HttpStatusCode.Created,
            MaxioStub.Subscription(500, MaxioStub.SentSubscriptionReference(body))));
        _maxio.On(HttpMethod.Get, SubscriptionLookupPath, (_, _) => MaxioStub.Json(HttpStatusCode.NotFound, ""));
    }

    private MaxioSubscriptionBillingService CreateService()
    {
        var client = new MaxioAdvancedBillingClient(new HttpClient(_maxio),
            MaxioServiceCollectionExtensions.CreateClientOptions(_settings, NullLoggerFactory.Instance));
        return new MaxioSubscriptionBillingService(client, NewContext(), Options.Create(_settings), TimeProvider.System,
            NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private CatalogContext NewContext() =>
        new(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(_databaseName).Options);

    [Fact]
    public async Task ListPlansExcludesArchivedProductsAndUsesBaseUrlVerbatim()
    {
        var plans = await CreateService().ListPlansAsync();

        Assert.Equal(new[] { "pro", "basic" }, plans.Plans.Select(p => p.Handle));
        Assert.Equal(299.00m, plans.Plans[0].Price);
        Assert.False(plans.Truncated);
        Assert.All(_maxio.Authorities, a => Assert.Equal(MaxioStub.BaseUrl, a));
    }

    [Fact]
    public async Task ListPlansMarksResultTruncatedAtPageCap()
    {
        var fullPage = "[" + string.Join(",", Enumerable.Range(0, MaxioSubscriptionBillingService.PlanPageSize)
            .Select(i => MaxioStub.Product($"p{i}", $"Plan {i}", 100))) + "]";
        _maxio.On(HttpMethod.Get, ProductsPath, (_, _) => MaxioStub.Json(HttpStatusCode.OK, fullPage));

        var plans = await CreateService().ListPlansAsync();

        Assert.True(plans.Truncated);
        Assert.Equal(MaxioSubscriptionBillingService.MaxPlanPages, _maxio.Count(HttpMethod.Get, ProductsPath));
    }

    [Fact]
    public async Task SubscribeCreatesCustomerAndSubscriptionWithReferences()
    {
        var result = await CreateService().SubscribeAsync(Shopper, "pro");

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(500, result.Subscription!.SubscriptionId);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(299.00m, result.Subscription.Price);
        Assert.Equal(new DateTimeOffset(2026, 11, 19, 0, 0, 0, TimeSpan.Zero), result.Subscription.NextBillingAt);

        var customerBody = _maxio.Requests.Single(r => r.Method == HttpMethod.Post && r.Path == CustomersPath).Body;
        Assert.Contains("\"reference\":\"demouser@microsoft.com\"", customerBody);
        var subscriptionBody = _maxio.Requests.Single(r => r.Method == HttpMethod.Post && r.Path == SubscriptionsPath).Body;
        Assert.Contains("\"product_handle\":\"pro\"", subscriptionBody);
        Assert.Contains("\"customer_id\":42", subscriptionBody);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", subscriptionBody);
        Assert.StartsWith("eshop-", MaxioStub.SentSubscriptionReference(subscriptionBody));
    }

    [Fact]
    public async Task RepeatSubscribeReturnsExistingWithoutSecondWrite()
    {
        await CreateService().SubscribeAsync(Shopper, "pro");
        _maxio.On(HttpMethod.Get, "/subscriptions/500.json", (_, _) => MaxioStub.Json(HttpStatusCode.OK, MaxioStub.Subscription(500, "ref")));

        var second = await CreateService().SubscribeAsync(Shopper, "pro");

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, second.Outcome);
        Assert.Equal(500, second.Subscription!.SubscriptionId);
        Assert.Equal(1, _maxio.Count(HttpMethod.Post, CustomersPath));
        Assert.Equal(1, _maxio.Count(HttpMethod.Post, SubscriptionsPath));
    }

    [Fact]
    public async Task ConcurrentDoubleSubmitIsRefusedBeforeReachingMaxio()
    {
        var firstReachedMaxio = new TaskCompletionSource();
        var releaseFirst = new TaskCompletionSource();
        _maxio.OnAsync(HttpMethod.Post, SubscriptionsPath, async (_, body) =>
        {
            firstReachedMaxio.TrySetResult();
            await releaseFirst.Task;
            return MaxioStub.Json(HttpStatusCode.Created, MaxioStub.Subscription(500, MaxioStub.SentSubscriptionReference(body)));
        });

        var first = CreateService().SubscribeAsync(Shopper, "pro");
        await firstReachedMaxio.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var second = await Assert.ThrowsAsync<BillingException>(() => CreateService().SubscribeAsync(Shopper, "pro"));
        releaseFirst.SetResult();

        Assert.Equal(BillingFailureKind.Conflict, second.Kind);
        Assert.Equal(SubscribeOutcome.Created, (await first).Outcome);
        Assert.Equal(1, _maxio.Count(HttpMethod.Post, SubscriptionsPath));
        Assert.Equal(1, _maxio.Count(HttpMethod.Post, CustomersPath));
    }

    [Fact]
    public async Task ConnectionFailureAfterSendIsSettledByReference()
    {
        string? sentReference = null;
        _maxio.On(HttpMethod.Post, SubscriptionsPath, (_, body) =>
        {
            sentReference = MaxioStub.SentSubscriptionReference(body);
            throw new HttpRequestException("connection reset after the request was sent");
        });
        _maxio.On(HttpMethod.Get, SubscriptionLookupPath, (request, _) =>
            request.RequestUri!.Query.Contains(sentReference!)
                ? MaxioStub.Json(HttpStatusCode.OK, MaxioStub.Subscription(777, sentReference!))
                : MaxioStub.Json(HttpStatusCode.NotFound, ""));

        var result = await CreateService().SubscribeAsync(Shopper, "pro");

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(777, result.Subscription!.SubscriptionId);
        Assert.Equal(1, _maxio.Count(HttpMethod.Post, SubscriptionsPath)); // never resent
        await using var db = NewContext();
        var claim = await db.MaxioSubscriptionClaims.SingleAsync();
        Assert.Equal(MaxioClaimStatus.Created, claim.Status);
        Assert.Equal(777, claim.MaxioSubscriptionId);
    }

    [Fact]
    public async Task UnconfirmedWriteIsPendingAndSettledByNextRequestWithoutResending()
    {
        string? sentReference = null;
        _maxio.On(HttpMethod.Post, SubscriptionsPath, (_, body) =>
        {
            sentReference = MaxioStub.SentSubscriptionReference(body);
            throw new HttpRequestException("connection reset after the request was sent");
        });

        var first = await CreateService().SubscribeAsync(Shopper, "pro");

        Assert.Equal(SubscribeOutcome.PendingConfirmation, first.Outcome);
        await using (var db = NewContext())
        {
            Assert.Equal(MaxioClaimStatus.Unknown, (await db.MaxioSubscriptionClaims.SingleAsync()).Status);
        }

        // The write did land at Maxio; it becomes visible by its reference.
        _maxio.On(HttpMethod.Get, SubscriptionLookupPath, (_, _) => MaxioStub.Json(HttpStatusCode.OK, MaxioStub.Subscription(888, sentReference!)));

        var second = await CreateService().SubscribeAsync(Shopper, "pro");

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, second.Outcome);
        Assert.Equal(888, second.Subscription!.SubscriptionId);
        Assert.Equal(1, _maxio.Count(HttpMethod.Post, SubscriptionsPath));
    }

    [Fact]
    public async Task CustomerCreateConnectionFailureIsSettledByReference()
    {
        var created = false;
        _maxio.On(HttpMethod.Post, CustomersPath, (_, _) =>
        {
            created = true;
            throw new HttpRequestException("connection reset after the request was sent");
        });
        _maxio.On(HttpMethod.Get, CustomerLookupPath, (_, _) => created
            ? MaxioStub.Json(HttpStatusCode.OK, """{"customer":{"id":42,"reference":"demouser@microsoft.com"}}""")
            : MaxioStub.Json(HttpStatusCode.NotFound, ""));

        var result = await CreateService().SubscribeAsync(Shopper, "pro");

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(1, _maxio.Count(HttpMethod.Post, CustomersPath));
        Assert.Contains("\"customer_id\":42", _maxio.Requests.Single(r => r.Path == SubscriptionsPath).Body);
    }

    [Fact]
    public async Task ExistingMaxioCustomerIsReusedNotRecreated()
    {
        _maxio.On(HttpMethod.Get, CustomerLookupPath, (_, _) =>
            MaxioStub.Json(HttpStatusCode.OK, """{"customer":{"id":42,"reference":"demouser@microsoft.com"}}"""));

        await CreateService().SubscribeAsync(Shopper, "pro");

        Assert.Equal(0, _maxio.Count(HttpMethod.Post, CustomersPath));
    }

    [Fact]
    public async Task LiveSubscriptionAtMaxioIsAdoptedWhenLocalStateIsLost()
    {
        _maxio.On(HttpMethod.Get, CustomerLookupPath, (_, _) =>
            MaxioStub.Json(HttpStatusCode.OK, """{"customer":{"id":42,"reference":"demouser@microsoft.com"}}"""));
        _maxio.On(HttpMethod.Get, CustomerSubscriptionsPath, (_, _) =>
            MaxioStub.Json(HttpStatusCode.OK, $"[{MaxioStub.Subscription(321, "eshop-earlier")}]"));

        var result = await CreateService().SubscribeAsync(Shopper, "pro");

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, result.Outcome);
        Assert.Equal(321, result.Subscription!.SubscriptionId);
        Assert.Equal(0, _maxio.Count(HttpMethod.Post, SubscriptionsPath));
    }

    [Fact]
    public async Task ProviderRejectionReleasesClaimSoUserCanRetry()
    {
        _maxio.On(HttpMethod.Post, SubscriptionsPath, (_, _) =>
            MaxioStub.Json(HttpStatusCode.UnprocessableEntity, """{"errors":["No payment method was on file"]}"""));

        var ex = await Assert.ThrowsAsync<BillingException>(() => CreateService().SubscribeAsync(Shopper, "pro"));

        Assert.Equal(BillingFailureKind.Rejected, ex.Kind);
        Assert.Contains("No payment method was on file", ex.Message);
        await using var db = NewContext();
        Assert.False(await db.MaxioSubscriptionClaims.AnyAsync());
    }

    [Fact]
    public async Task UnknownPlanIsRejectedWithoutAnyWrite()
    {
        var ex = await Assert.ThrowsAsync<BillingException>(() => CreateService().SubscribeAsync(Shopper, "old"));

        Assert.Equal(BillingFailureKind.InvalidRequest, ex.Kind);
        Assert.DoesNotContain(_maxio.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task ListSubscriptionsReadsFromMaxioForTheCaller()
    {
        _maxio.On(HttpMethod.Get, CustomerLookupPath, (_, _) =>
            MaxioStub.Json(HttpStatusCode.OK, """{"customer":{"id":42,"reference":"demouser@microsoft.com"}}"""));
        _maxio.On(HttpMethod.Get, CustomerSubscriptionsPath, (_, _) =>
            MaxioStub.Json(HttpStatusCode.OK, $"[{MaxioStub.Subscription(321, "eshop-earlier")}]"));

        var list = await CreateService().ListSubscriptionsAsync(Shopper);

        var subscription = Assert.Single(list.Subscriptions);
        Assert.Equal("pro", subscription.PlanHandle);
        Assert.False(list.PendingConfirmation);
    }

    [Fact]
    public async Task ListSubscriptionsIsEmptyForUserWithoutMaxioCustomer()
    {
        var list = await CreateService().ListSubscriptionsAsync(Shopper);

        Assert.Empty(list.Subscriptions);
    }

    [Fact]
    public async Task UnauthorizedProviderResponseIsReportedAsUnavailable()
    {
        _maxio.On(HttpMethod.Get, ProductsPath, (_, _) => MaxioStub.Json(HttpStatusCode.Unauthorized, ""));

        var ex = await Assert.ThrowsAsync<BillingException>(() => CreateService().ListPlansAsync());

        Assert.Equal(BillingFailureKind.Unavailable, ex.Kind);
    }
}
