using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private const string User = "shopper@example.com";
    private const string ProductsPath = "/product_families/handle:test-family/products.json";
    private const string CustomerLookupPath = "/customers/lookup.json";
    private const string CustomersPath = "/customers.json";
    private const string SubscriptionsPath = "/subscriptions.json";
    private const string SubscriptionLookupPath = "/subscriptions/lookup.json";
    private static readonly string Reference = SubscriptionEnrollment.BuildReference(User, "eshop-pro");

    private readonly string _databaseName = $"billing-{Guid.NewGuid()}";
    private readonly MaxioStub _maxio = new();

    public MaxioSubscriptionBillingServiceTests()
    {
        _maxio.On(HttpMethod.Get, ProductsPath, HttpStatusCode.OK,
            MaxioStub.ProductsJson((1, "eshop-pro", "Pro", 29900), (2, "basic-plan", "Basic", 2900)));
    }

    private CatalogContext NewContext() =>
        new(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(_databaseName).Options);

    private MaxioSubscriptionBillingService NewService(CatalogContext? context = null)
    {
        var settings = new MaxioSettings
        {
            ApiKey = "test-key",
            BaseUrl = "https://maxio.test",
            ProductFamilyHandle = "test-family"
        };
        var client = new MaxioAdvancedBillingClient(new HttpClient(_maxio),
            MaxioBillingServiceCollectionExtensions.BuildClientOptions(settings, NullLoggerFactory.Instance));
        return new MaxioSubscriptionBillingService(client, context ?? NewContext(), Options.Create(settings),
            new MemoryCache(new MemoryCacheOptions()), NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private void CustomerDoesNotExistYet()
    {
        _maxio.On(HttpMethod.Get, CustomerLookupPath, HttpStatusCode.NotFound, "{\"errors\":[\"Not Found\"]}");
        _maxio.On(HttpMethod.Post, CustomersPath, HttpStatusCode.Created, MaxioStub.CustomerJson(42, User));
    }

    private void SubscriptionDoesNotExistYet() =>
        _maxio.On(HttpMethod.Get, SubscriptionLookupPath, HttpStatusCode.NotFound, "");

    private async Task<SubscriptionEnrollment?> LoadEnrollment()
    {
        await using var context = NewContext();
        return await context.SubscriptionEnrollments.AsNoTracking().SingleOrDefaultAsync(e => e.SubscriptionReference == Reference);
    }

    // ------------------------------------------------------------ store guarantees the claims rely on

    [Fact]
    public async Task InMemoryStoreRefusesASecondClaimWithTheSamePrimaryKey()
    {
        await using (var first = NewContext())
        {
            first.BillingCustomers.Add(new BillingCustomer(User, DateTimeOffset.UtcNow));
            await first.SaveChangesAsync();
        }

        await using var second = NewContext();
        second.BillingCustomers.Add(new BillingCustomer(User, DateTimeOffset.UtcNow));
        var refusal = await Record.ExceptionAsync(() => second.SaveChangesAsync());

        // MaxioSubscriptionBillingService.IsStoreRefusal relies on this: SQL Server raises DbUpdateException,
        // the EF in-memory provider raises ArgumentException.
        Assert.IsType<ArgumentException>(refusal);
    }

    [Fact]
    public async Task InMemoryStoreLetsOnlyOneRequestTakeOverAClaim()
    {
        await using (var seed = NewContext())
        {
            var claim = new BillingCustomer(User, DateTimeOffset.UtcNow);
            claim.MarkUnknown(DateTimeOffset.UtcNow);
            seed.BillingCustomers.Add(claim);
            await seed.SaveChangesAsync();
        }

        await using var a = NewContext();
        await using var b = NewContext();
        var claimA = await a.BillingCustomers.SingleAsync();
        var claimB = await b.BillingCustomers.SingleAsync();
        claimA.TakeOver(DateTimeOffset.UtcNow);
        claimB.TakeOver(DateTimeOffset.UtcNow);
        await a.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => b.SaveChangesAsync());
    }

    // ------------------------------------------------------------ plans

    [Fact]
    public async Task ListsPlansOfTheConfiguredFamilyAtTheOverriddenBaseUrl()
    {
        var catalog = await NewService().ListPlansAsync();

        Assert.False(catalog.IsTruncated);
        Assert.Equal(new[] { "eshop-pro", "basic-plan" }, catalog.Plans.Select(p => p.Handle));
        var pro = catalog.Plans[0];
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal("month", pro.IntervalUnit);
        var request = Assert.Single(_maxio.Requests);
        Assert.Contains("per_page=200", request.PathAndQuery);
    }

    [Fact]
    public async Task FlagsThePlanListAsTruncatedWhenThePageCapIsReached()
    {
        var fullPage = MaxioStub.ProductsJson(Enumerable.Range(1, MaxioSubscriptionBillingService.PlanPageSize)
            .Select(i => (i, $"plan-{i}", $"Plan {i}", 100L)).ToArray());
        _maxio.On(HttpMethod.Get, ProductsPath, HttpStatusCode.OK, fullPage);

        var catalog = await NewService().ListPlansAsync();

        Assert.True(catalog.IsTruncated);
        Assert.Equal(MaxioSubscriptionBillingService.MaxPlanPages, _maxio.Count(HttpMethod.Get, ProductsPath));
    }

    // ------------------------------------------------------------ subscribe

    [Fact]
    public async Task SubscribeCreatesTheCustomerAndTheSubscriptionAndRecordsBothClaims()
    {
        CustomerDoesNotExistYet();
        SubscriptionDoesNotExistYet();
        _maxio.On(HttpMethod.Post, SubscriptionsPath, HttpStatusCode.Created, MaxioStub.SubscriptionJson(777, Reference));

        var result = await NewService().SubscribeAsync(User, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(777, result.Subscription.SubscriptionId);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(29900, result.Subscription.PriceInCents);
        Assert.Equal(DateTimeOffset.Parse("2026-11-20T10:00:00-05:00"), result.Subscription.NextBillingAt);

        var customerBody = _maxio.Requests.Single(r => r.Method == HttpMethod.Post && r.PathAndQuery == CustomersPath).Body;
        Assert.Contains($"\"reference\":\"{User}\"", customerBody);
        var subscriptionBody = _maxio.Requests.Single(r => r.Method == HttpMethod.Post && r.PathAndQuery == SubscriptionsPath).Body;
        Assert.Contains("\"product_handle\":\"eshop-pro\"", subscriptionBody);
        Assert.Contains("\"customer_id\":42", subscriptionBody);
        Assert.Contains($"\"reference\":\"{Reference}\"", subscriptionBody);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", subscriptionBody);

        var enrollment = await LoadEnrollment();
        Assert.Equal(BillingClaimStatus.Completed, enrollment!.Status);
        Assert.Equal(777, enrollment.ProviderId);
    }

    [Fact]
    public async Task SubscribingTwiceReturnsTheExistingSubscriptionWithoutCreatingAnother()
    {
        CustomerDoesNotExistYet();
        SubscriptionDoesNotExistYet();
        _maxio.On(HttpMethod.Post, SubscriptionsPath, HttpStatusCode.Created, MaxioStub.SubscriptionJson(777, Reference));
        _maxio.On(HttpMethod.Get, "/subscriptions/777.json", HttpStatusCode.OK, MaxioStub.SubscriptionJson(777, Reference));

        await NewService().SubscribeAsync(User, "eshop-pro");
        var second = await NewService().SubscribeAsync(User, "eshop-pro");

        Assert.False(second.Created);
        Assert.Equal(777, second.Subscription.SubscriptionId);
        Assert.Equal(1, _maxio.Count(HttpMethod.Post, CustomersPath));
        Assert.Equal(1, _maxio.Count(HttpMethod.Post, SubscriptionsPath));
    }

    [Fact]
    public async Task AConcurrentDoubleSubmitReachesMaxioOnlyOnce()
    {
        CustomerDoesNotExistYet();
        SubscriptionDoesNotExistYet();
        var firstCreateArrived = new TaskCompletionSource();
        var releaseFirstCreate = new TaskCompletionSource();
        _maxio.OnAsync(HttpMethod.Post, SubscriptionsPath, async _ =>
        {
            firstCreateArrived.TrySetResult();
            await releaseFirstCreate.Task;
            return MaxioStub.Json(HttpStatusCode.Created, MaxioStub.SubscriptionJson(777, Reference));
        });

        var first = NewService().SubscribeAsync(User, "eshop-pro");
        await firstCreateArrived.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // The second click arrives while the first is still waiting on Maxio: the store refuses its claim.
        var second = await Assert.ThrowsAsync<SubscriptionBillingException>(() => NewService().SubscribeAsync(User, "eshop-pro"));
        Assert.Equal(SubscriptionBillingFailure.InProgress, second.Failure);

        releaseFirstCreate.SetResult();
        Assert.True((await first).Created);
        Assert.Equal(1, _maxio.Count(HttpMethod.Post, CustomersPath));
        Assert.Equal(1, _maxio.Count(HttpMethod.Post, SubscriptionsPath));
    }

    [Fact]
    public async Task AnUnknownPlanIsRejectedBeforeAnythingIsWritten()
    {
        var error = await Assert.ThrowsAsync<SubscriptionBillingException>(() => NewService().SubscribeAsync(User, "not-a-plan"));

        Assert.Equal(SubscriptionBillingFailure.InvalidRequest, error.Failure);
        Assert.DoesNotContain(_maxio.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task ReusesAnExistingMaxioCustomerFoundByReference()
    {
        _maxio.On(HttpMethod.Get, CustomerLookupPath, HttpStatusCode.OK, MaxioStub.CustomerJson(99, User));
        SubscriptionDoesNotExistYet();
        _maxio.On(HttpMethod.Post, SubscriptionsPath, HttpStatusCode.Created, MaxioStub.SubscriptionJson(777, Reference));

        await NewService().SubscribeAsync(User, "eshop-pro");

        Assert.Equal(0, _maxio.Count(HttpMethod.Post, CustomersPath));
        Assert.Contains("\"customer_id\":99", _maxio.Requests.Single(r => r.Method == HttpMethod.Post).Body);
    }

    [Fact]
    public async Task AProviderRejectionReleasesTheClaimAndCarriesMaxiosReasons()
    {
        CustomerDoesNotExistYet();
        SubscriptionDoesNotExistYet();
        _maxio.On(HttpMethod.Post, SubscriptionsPath, HttpStatusCode.UnprocessableEntity,
            "{\"errors\":[\"Product is not available.\"]}");

        var error = await Assert.ThrowsAsync<SubscriptionBillingException>(() => NewService().SubscribeAsync(User, "eshop-pro"));

        Assert.Equal(SubscriptionBillingFailure.Rejected, error.Failure);
        Assert.Contains("Product is not available.", error.Details);
        Assert.Null(await LoadEnrollment());
    }

    // ------------------------------------------------------------ unknown outcomes (failed connections)

    [Fact]
    public async Task ALostConnectionOnCreateSubscriptionIsSettledByReference()
    {
        CustomerDoesNotExistYet();
        var lookups = 0;
        _maxio.On(HttpMethod.Get, SubscriptionLookupPath, _ => ++lookups == 1
            ? MaxioStub.Json(HttpStatusCode.NotFound, "")                                     // pre-check: not there yet
            : MaxioStub.Json(HttpStatusCode.OK, MaxioStub.SubscriptionJson(777, Reference)));  // settlement: it landed
        _maxio.On(HttpMethod.Post, SubscriptionsPath, _ => throw new HttpRequestException("connection reset"));

        var result = await NewService().SubscribeAsync(User, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(777, result.Subscription.SubscriptionId);
        Assert.Equal(1, _maxio.Count(HttpMethod.Post, SubscriptionsPath)); // the SDK never resends the POST
        Assert.Equal(BillingClaimStatus.Completed, (await LoadEnrollment())!.Status);
    }

    [Fact]
    public async Task AnUnsettledOutcomeIsRecordedAndSettledByTheNextRequestWithoutASecondCreate()
    {
        CustomerDoesNotExistYet();
        SubscriptionDoesNotExistYet();
        _maxio.On(HttpMethod.Get, "/customers/42/subscriptions.json", HttpStatusCode.OK, "[]");
        _maxio.On(HttpMethod.Post, SubscriptionsPath, _ => throw new HttpRequestException("connection reset"));

        var error = await Assert.ThrowsAsync<SubscriptionBillingException>(() => NewService().SubscribeAsync(User, "eshop-pro"));
        Assert.Equal(SubscriptionBillingFailure.OutcomeUnknown, error.Failure);
        Assert.Equal(BillingClaimStatus.Unknown, (await LoadEnrollment())!.Status);

        // The write did land after all: the retry finds it by reference instead of creating a second one.
        _maxio.On(HttpMethod.Get, SubscriptionLookupPath, HttpStatusCode.OK, MaxioStub.SubscriptionJson(777, Reference));
        var retry = await NewService().SubscribeAsync(User, "eshop-pro");

        Assert.Equal(777, retry.Subscription.SubscriptionId);
        Assert.Equal(1, _maxio.Count(HttpMethod.Post, SubscriptionsPath));
        Assert.Equal(BillingClaimStatus.Completed, (await LoadEnrollment())!.Status);
    }

    [Fact]
    public async Task ALostConnectionOnCreateCustomerIsSettledByReference()
    {
        var lookups = 0;
        _maxio.On(HttpMethod.Get, CustomerLookupPath, _ => ++lookups == 1
            ? MaxioStub.Json(HttpStatusCode.NotFound, "{\"errors\":[\"Not Found\"]}")
            : MaxioStub.Json(HttpStatusCode.OK, MaxioStub.CustomerJson(42, User)));
        _maxio.On(HttpMethod.Post, CustomersPath, _ => throw new HttpRequestException("connection reset"));
        SubscriptionDoesNotExistYet();
        _maxio.On(HttpMethod.Post, SubscriptionsPath, HttpStatusCode.Created, MaxioStub.SubscriptionJson(777, Reference));

        var result = await NewService().SubscribeAsync(User, "eshop-pro");

        Assert.Equal(777, result.Subscription.SubscriptionId);
        Assert.Equal(1, _maxio.Count(HttpMethod.Post, CustomersPath));
        await using var context = NewContext();
        var link = await context.BillingCustomers.SingleAsync();
        Assert.Equal(BillingClaimStatus.Completed, link.Status);
        Assert.Equal(42, link.ProviderId);
    }

    // ------------------------------------------------------------ my subscriptions

    [Fact]
    public async Task ListsNothingForAShopperWhoNeverSubscribed()
    {
        _maxio.On(HttpMethod.Get, CustomerLookupPath, HttpStatusCode.NotFound, "{\"errors\":[\"Not Found\"]}");

        var subscriptions = await NewService().ListSubscriptionsAsync(User);

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task ListsTheShoppersSubscriptionsFromMaxio()
    {
        _maxio.On(HttpMethod.Get, CustomerLookupPath, HttpStatusCode.OK, MaxioStub.CustomerJson(42, User));
        _maxio.On(HttpMethod.Get, "/customers/42/subscriptions.json", HttpStatusCode.OK,
            MaxioStub.SubscriptionListJson(MaxioStub.SubscriptionJson(777, Reference)));

        var subscriptions = await NewService().ListSubscriptionsAsync(User);

        var subscription = Assert.Single(subscriptions);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal("active", subscription.State);
    }

    [Fact]
    public async Task MapsMaxioCredentialFailuresToProviderUnavailable()
    {
        _maxio.On(HttpMethod.Get, ProductsPath, HttpStatusCode.Unauthorized, "");

        var error = await Assert.ThrowsAsync<SubscriptionBillingException>(() => NewService().ListPlansAsync());

        Assert.Equal(SubscriptionBillingFailure.ProviderUnavailable, error.Failure);
    }
}
