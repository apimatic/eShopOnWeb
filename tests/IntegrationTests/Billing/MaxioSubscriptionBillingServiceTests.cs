using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

public class MaxioSubscriptionBillingServiceTests : IDisposable
{
    private const string BaseUrl = "https://maxio.test";
    private static readonly BillingUser User = new("user-1", "eshop-demouser@example.com", "demouser@example.com");

    private readonly FakeMaxio _maxio = new();
    private readonly ServiceProvider _provider;

    public MaxioSubscriptionBillingServiceTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Maxio:ApiKey"] = "test-api-key",
                ["Maxio:Subdomain"] = "ignored-when-base-url-is-set",
                ["Maxio:ProductFamilyHandle"] = "test-family",
                ["Maxio:BaseUrl"] = BaseUrl,
                ["Maxio:DefaultPlanHandle"] = "pro"
            })
            .Build();

        var databaseName = "billing-" + Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddMemoryCache();
        services.AddDbContext<CatalogContext>(o => o.UseInMemoryDatabase(databaseName));
        services.AddMaxioSubscriptionBilling();
        services.AddHttpClient(MaxioBillingServiceCollectionExtensions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => _maxio);
        _provider = services.BuildServiceProvider();
    }

    public void Dispose() => _provider.Dispose();

    /// <summary>A fresh scope per call, as each HTTP request gets its own DbContext.</summary>
    private async Task<T> InScope<T>(Func<ISubscriptionBillingService, Task<T>> action)
    {
        using var scope = _provider.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ISubscriptionBillingService>());
    }

    private async Task<T> InDb<T>(Func<CatalogContext, Task<T>> action)
    {
        using var scope = _provider.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<CatalogContext>());
    }

    [Fact]
    public async Task GetPlans_ReturnsOfferablePlans_FromConfiguredFamily_ViaBaseUrlOverride()
    {
        _maxio.ProductsPage = page => page == 1
            ? FakeMaxio.Json(new[]
            {
                FakeMaxio.Product(1, "pro", "Pro Plan", 29900),
                FakeMaxio.Product(2, "card-only", "Card Plan", 1000, requireCreditCard: true),
                FakeMaxio.Product(3, "old", "Old Plan", 500, archivedAt: "2025-01-01T00:00:00Z")
            })
            : "[]";

        var catalog = await InScope(s => s.GetPlansAsync());

        var plan = Assert.Single(catalog.Plans);
        Assert.Equal("pro", plan.Handle);
        Assert.Equal(299.00m, plan.Price);
        Assert.Equal("month", plan.IntervalUnit);
        Assert.False(catalog.IsTruncated);

        var request = Assert.Single(_maxio.Requests);
        Assert.Equal("/product_families/handle:test-family/products.json", request.Path);
        Assert.Contains("per_page=200", request.Query);
        // Basic auth: the API key is the username and "x" the password.
        Assert.Equal("Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("test-api-key:x")), request.Authorization);
    }

    [Fact]
    public async Task GetPlans_FlagsTruncation_WhenPageCapIsHit()
    {
        _maxio.ProductsPage = page => FakeMaxio.Json(Enumerable.Range(1, 200)
            .Select(i => FakeMaxio.Product(page * 1000 + i, $"p{page}-{i}", "Plan", 100)).ToArray());

        var catalog = await InScope(s => s.GetPlansAsync());

        Assert.True(catalog.IsTruncated);
        Assert.Equal(1000, catalog.Plans.Count);
        Assert.Equal(5, _maxio.Count(HttpMethod.Get, "/products.json"));
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerAndSubscription_AndConfirmsDetails()
    {
        var result = await InScope(s => s.SubscribeAsync(User, null));

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal("pro", result.PlanHandle);
        Assert.NotNull(result.Subscription);
        Assert.Equal(FakeMaxio.SubscriptionId, result.Subscription!.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(299.00m, result.Subscription.Price);
        Assert.Equal(new DateTimeOffset(2026, 11, 19, 0, 0, 0, TimeSpan.Zero), result.Subscription.NextBillingAt);

        var createCustomer = Assert.Single(_maxio.Requests, r => r.Method == HttpMethod.Post && r.Path == "/customers.json");
        using (var doc = JsonDocument.Parse(createCustomer.Body!))
        {
            var customer = doc.RootElement.GetProperty("customer");
            Assert.Equal(User.CustomerReference, customer.GetProperty("reference").GetString());
            Assert.Equal(User.Email, customer.GetProperty("email").GetString());
        }

        var createSubscription = Assert.Single(_maxio.Requests, r => r.Method == HttpMethod.Post && r.Path == "/subscriptions.json");
        using (var doc = JsonDocument.Parse(createSubscription.Body!))
        {
            var subscription = doc.RootElement.GetProperty("subscription");
            Assert.Equal("pro", subscription.GetProperty("product_handle").GetString());
            Assert.Equal(FakeMaxio.CustomerId, subscription.GetProperty("customer_id").GetInt32());
            Assert.Equal(result.Reference, subscription.GetProperty("reference").GetString());
            Assert.Equal("remittance", subscription.GetProperty("payment_collection_method").GetString());
        }

        var claim = await InDb(db => db.SubscriptionClaims.SingleAsync());
        Assert.Equal(ClaimStatus.Completed, claim.Status);
        Assert.Equal(FakeMaxio.SubscriptionId, claim.ProviderSubscriptionId);
        var link = await InDb(db => db.BillingCustomerLinks.SingleAsync());
        Assert.Equal(FakeMaxio.CustomerId, link.ProviderCustomerId);
    }

    [Fact]
    public async Task Subscribe_Twice_ReturnsExistingSubscription_WithoutSecondCreate()
    {
        await InScope(s => s.SubscribeAsync(User, "pro"));
        var second = await InScope(s => s.SubscribeAsync(User, "pro"));

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, second.Outcome);
        Assert.Equal(FakeMaxio.SubscriptionId, second.Subscription!.Id);
        Assert.Equal(1, _maxio.Count(HttpMethod.Post, "/subscriptions.json"));
        Assert.Equal(1, _maxio.Count(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public async Task Subscribe_ConcurrentDoubleSubmit_SendsExactlyOneCreate()
    {
        var release = new TaskCompletionSource();
        _maxio.OnCreateSubscription = async (_, body) =>
        {
            await release.Task; // hold the first create in flight while the second request arrives
            var sub = FakeMaxio.SubscriptionFrom(body);
            _maxio.Subscriptions.Add(sub);
            return FakeMaxio.Ok(FakeMaxio.Json(new { subscription = sub }));
        };
        await InScope(s => s.GetPlansAsync()); // warm the plan cache so both requests race on the claim

        var first = InScope(s => s.SubscribeAsync(User, "pro"));
        var second = InScope(s => s.SubscribeAsync(User, "pro"));
        var settledFirst = await Task.WhenAny(first, second);
        var rejected = await Assert.ThrowsAsync<SubscriptionBillingException>(() => settledFirst);
        release.SetResult();
        var winner = await (settledFirst == first ? second : first);

        Assert.Equal(409, rejected.StatusCode);
        Assert.Equal(SubscribeOutcome.Created, winner.Outcome);
        Assert.Equal(1, _maxio.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task Subscribe_RejectsPlanNotInCatalog_WithoutCallingCreate()
    {
        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(() => InScope(s => s.SubscribeAsync(User, "not-a-plan")));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(0, _maxio.Count(HttpMethod.Post, "/subscriptions.json"));
        Assert.Equal(0, await InDb(db => db.SubscriptionClaims.CountAsync()));
    }

    [Fact]
    public async Task Subscribe_ProviderDeclines_Returns422_AndReleasesClaim()
    {
        _maxio.OnCreateSubscription = (_, _) => Task.FromResult(FakeMaxio.Status(HttpStatusCode.UnprocessableEntity,
            "{\"errors\":[\"No payment method was on file\"]}"));

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(() => InScope(s => s.SubscribeAsync(User, "pro")));

        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("No payment method was on file", ex.Message);
        Assert.Equal(0, await InDb(db => db.SubscriptionClaims.CountAsync()));

        // The released claim lets the next attempt through.
        _maxio.OnCreateSubscription = null;
        var retry = await InScope(s => s.SubscribeAsync(User, "pro"));
        Assert.Equal(SubscribeOutcome.Created, retry.Outcome);
    }

    [Fact]
    public async Task Subscribe_ConnectionDropsAfterSend_ReconcilesByReference_AsCreated()
    {
        string? sentReference = null;
        _maxio.OnCreateSubscription = (_, body) =>
        {
            var sub = FakeMaxio.SubscriptionFrom(body);
            _maxio.Subscriptions.Add(sub);
            sentReference = JsonDocument.Parse(body!).RootElement.GetProperty("subscription").GetProperty("reference").GetString();
            throw new HttpRequestException("connection reset after the provider acted");
        };
        _maxio.OnFindSubscription = request =>
        {
            Assert.Contains("reference=" + sentReference, request.RequestUri!.Query);
            var sub = _maxio.Subscriptions.Single();
            return Task.FromResult(FakeMaxio.Ok(FakeMaxio.Json(new { subscription = sub })));
        };

        var result = await InScope(s => s.SubscribeAsync(User, "pro"));

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(sentReference, result.Reference);
        Assert.Equal(1, _maxio.Count(HttpMethod.Post, "/subscriptions.json")); // POST is never resent
        Assert.Equal(ClaimStatus.Completed, (await InDb(db => db.SubscriptionClaims.SingleAsync())).Status);
    }

    [Fact]
    public async Task Subscribe_ConnectionFails_AndReferenceNotFound_Returns503_AndReleasesClaim()
    {
        _maxio.OnCreateSubscription = (_, _) => throw new HttpRequestException("connection refused");
        // OnFindSubscription unset: the fake answers 404 (no such reference).

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(() => InScope(s => s.SubscribeAsync(User, "pro")));

        Assert.Equal(503, ex.StatusCode);
        Assert.Equal(1, _maxio.Count(HttpMethod.Get, "/subscriptions/lookup.json"));
        Assert.Equal(0, await InDb(db => db.SubscriptionClaims.CountAsync()));
    }

    [Fact]
    public async Task Subscribe_OutcomeUnknownAndReReadFails_ReturnsPending_ThenMySubscriptionsSettlesIt()
    {
        _maxio.OnCreateSubscription = (_, body) =>
        {
            _maxio.Subscriptions.Add(FakeMaxio.SubscriptionFrom(body));
            throw new HttpRequestException("connection reset");
        };
        _maxio.OnFindSubscription = _ => throw new HttpRequestException("still down");

        var pending = await InScope(s => s.SubscribeAsync(User, "pro"));

        Assert.Equal(SubscribeOutcome.PendingConfirmation, pending.Outcome);
        var claim = await InDb(db => db.SubscriptionClaims.SingleAsync());
        Assert.Equal(ClaimStatus.Pending, claim.Status);
        Assert.True(claim.OutcomeUnknown);

        // A repeat while unknown must not send a second create: it settles the first one instead.
        _maxio.OnFindSubscription = _ => Task.FromResult(FakeMaxio.Ok(FakeMaxio.Json(new { subscription = _maxio.Subscriptions.Single() })));
        var subscriptions = await InScope(s => s.GetSubscriptionsAsync(User));

        Assert.Single(subscriptions);
        Assert.Equal(ClaimStatus.Completed, (await InDb(db => db.SubscriptionClaims.SingleAsync())).Status);
        Assert.Equal(1, _maxio.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task Subscribe_AdoptsLiveSubscriptionAlreadyAtMaxio_WhenLocalRecordsWereLost()
    {
        _maxio.OnCustomerLookup = _ => Task.FromResult(FakeMaxio.Ok(FakeMaxio.CustomerJson(User.CustomerReference)));
        _maxio.Subscriptions.Add(FakeMaxio.SubscriptionObject(777, "eshop-sub-from-a-previous-run", "pro", "active"));

        var result = await InScope(s => s.SubscribeAsync(User, "pro"));

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, result.Outcome);
        Assert.Equal(777, result.Subscription!.Id);
        Assert.Equal(0, _maxio.Count(HttpMethod.Post, "/customers.json"));
        Assert.Equal(0, _maxio.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task Subscribe_CustomerCreateConnectionFails_ReconcilesCustomerByReference()
    {
        var created = false;
        _maxio.OnCreateCustomer = (_, _) =>
        {
            created = true;
            throw new HttpRequestException("connection reset");
        };
        _maxio.OnCustomerLookup = _ => Task.FromResult(created
            ? FakeMaxio.Ok(FakeMaxio.CustomerJson(User.CustomerReference))
            : FakeMaxio.Status(HttpStatusCode.NotFound, ""));

        var result = await InScope(s => s.SubscribeAsync(User, "pro"));

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(1, _maxio.Count(HttpMethod.Post, "/customers.json"));
        Assert.Equal(FakeMaxio.CustomerId, (await InDb(db => db.BillingCustomerLinks.SingleAsync())).ProviderCustomerId);
    }

    [Fact]
    public async Task Subscribe_ProviderRejectsCredentials_MapsTo502_WithoutLeakingProviderDetail()
    {
        _maxio.OnListCustomerSubscriptions = _ => Task.FromResult(FakeMaxio.Status(HttpStatusCode.Unauthorized, "HTTP Basic: Access denied."));
        _maxio.OnCustomerLookup = _ => Task.FromResult(FakeMaxio.Ok(FakeMaxio.CustomerJson(User.CustomerReference)));

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(() => InScope(s => s.GetSubscriptionsAsync(User)));

        Assert.Equal(502, ex.StatusCode);
        Assert.DoesNotContain("maxio.test", ex.Message);
        Assert.DoesNotContain("Access denied", ex.Message);
    }

    [Fact]
    public async Task GetSubscriptions_ReturnsEmpty_WhenUserHasNoMaxioCustomer()
    {
        var subscriptions = await InScope(s => s.GetSubscriptionsAsync(User));

        Assert.Empty(subscriptions);
        Assert.Equal(0, _maxio.Count(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public void Settings_FailFast_NamesMissingKeys_WithoutValues()
    {
        var validator = new MaxioSettingsValidator();

        var result = validator.Validate(null, new MaxioSettings { BaseUrl = "not a url" });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Maxio:ApiKey"));
        Assert.Contains(result.Failures!, f => f.Contains("Maxio:ProductFamilyHandle"));
        Assert.Contains(result.Failures!, f => f.Contains("Maxio:BaseUrl"));
        Assert.True(validator.Validate(null, new MaxioSettings { ApiKey = "k", Subdomain = "site", ProductFamilyHandle = "f" }).Succeeded);
    }

    [Fact]
    public void Settings_MissingApiKey_FailsWhenOptionsAreResolved()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Maxio:Subdomain"] = "site", ["Maxio:ProductFamilyHandle"] = "f" })
            .Build());
        services.AddMaxioSubscriptionBilling();
        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<MaxioSettings>>().Value);
        Assert.Contains("Maxio:ApiKey", ex.Message);
    }
}
