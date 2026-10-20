using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingClaimAggregate;
using Microsoft.eShopWeb.PublicApi.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.PublicApiUnitTests.Billing;

public class SubscriptionBillingServiceTests : IDisposable
{
    private const string UserName = "demouser@microsoft.com";
    private const string ProductsPath = "/product_families/handle:eshop-subscribe/products.json";
    private const string CustomerLookupPath = "/customers/lookup.json";
    private const string CustomersPath = "/customers.json";
    private const string SubscriptionLookupPath = "/subscriptions/lookup.json";
    private const string SubscriptionsPath = "/subscriptions.json";
    private const string CustomerSubscriptionsPath = "/customers/42/subscriptions.json";

    private static readonly string CustomerReference = BillingReferences.ForCustomer(UserName);
    private static readonly string ProSubscriptionReference = BillingReferences.ForSubscription(CustomerReference, "eshop-pro");

    private readonly ClaimDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private sealed class FixedProfiles : IShopperProfileProvider
    {
        public Task<ShopperProfile?> GetAsync(string userName) => Task.FromResult<ShopperProfile?>(ShopperProfile.FromEmail(userName));
    }

    /// <summary>One service per simulated inbound request: its own DI scope (claim store) and gateway budget.</summary>
    private async Task<T> InRequest<T>(StubHandler stub, Func<SubscriptionBillingService, Task<T>> action, MaxioGatewayLimits? limits = null)
    {
        using var scope = _db.NewScope();
        using var gateway = MaxioTestFactory.Gateway(stub, limits);
        var service = new SubscriptionBillingService(gateway, _db.Store(scope), new FixedProfiles(), TimeProvider.System,
            NullLogger<SubscriptionBillingService>.Instance);
        return await action(service);
    }

    private static StubHandler NewProviderWithPlans() =>
        new StubHandler().OnJson(HttpMethod.Get, ProductsPath, HttpStatusCode.OK, MaxioJson.StandardPlans());

    [Fact]
    public async Task SubscribeCreatesCustomerAndSubscriptionThenRepeatReturnsTheExistingOne()
    {
        var stub = NewProviderWithPlans()
            .OnJson(HttpMethod.Get, CustomerLookupPath, HttpStatusCode.NotFound, "{}")
            .OnJson(HttpMethod.Post, CustomersPath, HttpStatusCode.Created, MaxioJson.Customer(42, CustomerReference))
            .OnJson(HttpMethod.Get, SubscriptionLookupPath, HttpStatusCode.NotFound, "")
            .OnJson(HttpMethod.Get, CustomerSubscriptionsPath, HttpStatusCode.OK, "[]")
            .OnJson(HttpMethod.Post, SubscriptionsPath, HttpStatusCode.Created, MaxioJson.Subscription(500, ProSubscriptionReference));

        var first = await InRequest(stub, s => s.SubscribeAsync(UserName, "eshop-pro", CancellationToken.None));

        Assert.True(first.Created);
        Assert.Equal(500, first.Subscription.Id);
        Assert.Equal("active", first.Subscription.State);
        var customerClaim = await _db.ReadAsync<BillingCustomerClaim>(CustomerReference);
        Assert.Equal(BillingClaimStatus.Created, customerClaim!.Status);
        Assert.Equal(42, customerClaim.ProviderId);
        var subscriptionClaim = await _db.ReadAsync<BillingSubscriptionClaim>(ProSubscriptionReference);
        Assert.Equal(BillingClaimStatus.Created, subscriptionClaim!.Status);
        Assert.Equal(500, subscriptionClaim.ProviderId);

        // Double-click: the provider now holds the subscription by reference.
        stub.OnJson(HttpMethod.Get, SubscriptionLookupPath, HttpStatusCode.OK, MaxioJson.Subscription(500, ProSubscriptionReference));
        var second = await InRequest(stub, s => s.SubscribeAsync(UserName, "eshop-pro", CancellationToken.None));

        Assert.False(second.Created);
        Assert.Equal(500, second.Subscription.Id);
        Assert.Equal(1, stub.Count(HttpMethod.Post, CustomersPath));
        Assert.Equal(1, stub.Count(HttpMethod.Post, SubscriptionsPath));
    }

    [Fact]
    public async Task CustomerReferenceSentToTheProviderCarriesNoPersonalData()
    {
        var stub = NewProviderWithPlans()
            .OnJson(HttpMethod.Get, CustomerLookupPath, HttpStatusCode.NotFound, "{}")
            .OnJson(HttpMethod.Post, CustomersPath, HttpStatusCode.Created, MaxioJson.Customer(42, CustomerReference))
            .OnJson(HttpMethod.Get, SubscriptionLookupPath, HttpStatusCode.NotFound, "")
            .OnJson(HttpMethod.Get, CustomerSubscriptionsPath, HttpStatusCode.OK, "[]")
            .OnJson(HttpMethod.Post, SubscriptionsPath, HttpStatusCode.Created, MaxioJson.Subscription(500, ProSubscriptionReference));

        await InRequest(stub, s => s.SubscribeAsync(UserName, "eshop-pro", CancellationToken.None));

        Assert.StartsWith("eshop-user-", CustomerReference);
        Assert.DoesNotContain("demouser", CustomerReference);
        Assert.All(stub.Requests.Where(r => r.Method == HttpMethod.Get), r => Assert.DoesNotContain("demouser", r.RequestUri!.ToString()));
    }

    [Fact]
    public async Task UnknownPlanHandleIsRejectedBeforeAnyWrite()
    {
        var stub = NewProviderWithPlans();

        var ex = await Assert.ThrowsAsync<InvalidSubscriptionRequestException>(() =>
            InRequest(stub, s => s.SubscribeAsync(UserName, "no-such-plan", CancellationToken.None)));

        Assert.Contains("planHandle", ex.Message);
        Assert.DoesNotContain(stub.Requests, r => r.Method == HttpMethod.Post);
        Assert.Null(await _db.ReadAsync<BillingCustomerClaim>(CustomerReference));
    }

    [Fact]
    public async Task ArchivedPlanHandleIsRejected()
    {
        var stub = NewProviderWithPlans();

        await Assert.ThrowsAsync<InvalidSubscriptionRequestException>(() =>
            InRequest(stub, s => s.SubscribeAsync(UserName, "old-plan", CancellationToken.None)));
    }

    [Fact]
    public async Task MissingPlanHandleIsRejected()
    {
        var stub = NewProviderWithPlans();

        await Assert.ThrowsAsync<InvalidSubscriptionRequestException>(() =>
            InRequest(stub, s => s.SubscribeAsync(UserName, " ", CancellationToken.None)));
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task PlanMissingFromATruncatedListCannotBeValidated()
    {
        var stub = new StubHandler().OnJson(HttpMethod.Get, ProductsPath, HttpStatusCode.OK, MaxioJson.Products(
            MaxioJson.Product(1, "plan-a", "A", 100), MaxioJson.Product(2, "plan-b", "B", 200)));
        using var scope = _db.NewScope();
        using var gateway = MaxioTestFactory.Gateway(stub, new MaxioGatewayLimits { PlansPerPage = 2, MaxPlanPages = 1 });
        var service = new SubscriptionBillingService(gateway, _db.Store(scope), new FixedProfiles(), TimeProvider.System,
            NullLogger<SubscriptionBillingService>.Instance);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => service.SubscribeAsync(UserName, "eshop-pro", CancellationToken.None));

        Assert.Equal(BillingFailureKind.CatalogIncomplete, ex.Kind);
    }

    [Fact]
    public async Task ConcurrentSubscribeWhileAClaimIsInFlightIsRefusedByTheStoreAndReportedInProgress()
    {
        // Another request holds a fresh Pending claim for the same subscription reference.
        using (var other = _db.NewScope())
        {
            var customer = new BillingCustomerClaim(CustomerReference, DateTimeOffset.UtcNow);
            await _db.Store(other).TryInsertAsync(customer);
            customer.MarkCreated(42, DateTimeOffset.UtcNow);
            await _db.Store(other).TryUpdateAsync(customer);
            await _db.Store(other).TryInsertAsync(new BillingSubscriptionClaim(ProSubscriptionReference, CustomerReference, "eshop-pro", DateTimeOffset.UtcNow));
        }
        var stub = NewProviderWithPlans();

        var ex = await Assert.ThrowsAsync<BillingOperationPendingException>(() =>
            InRequest(stub, s => s.SubscribeAsync(UserName, "eshop-pro", CancellationToken.None)));

        Assert.Equal(BillingPendingKind.InFlight, ex.Kind);
        Assert.DoesNotContain(stub.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task UnsettledCustomerWriteIsRecordedUnknownAndSettledOnTheNextRequest()
    {
        var stub = NewProviderWithPlans()
            .OnJson(HttpMethod.Get, CustomerLookupPath, HttpStatusCode.NotFound, "{}")
            .On(HttpMethod.Post, CustomersPath, _ => throw new HttpRequestException("connection reset"));

        var ex = await Assert.ThrowsAsync<BillingOperationPendingException>(() =>
            InRequest(stub, s => s.SubscribeAsync(UserName, "eshop-pro", CancellationToken.None)));

        Assert.Equal(BillingPendingKind.OutcomeUnknown, ex.Kind);
        Assert.Equal(CustomerReference, ex.Reference);
        var claim = await _db.ReadAsync<BillingCustomerClaim>(CustomerReference);
        Assert.Equal(BillingClaimStatus.Unknown, claim!.Status); // not deleted, not "failed"

        // The write had in fact landed; the next request settles by reference before any new write.
        stub.OnJson(HttpMethod.Get, CustomerLookupPath, HttpStatusCode.OK, MaxioJson.Customer(42, CustomerReference))
            .OnJson(HttpMethod.Get, SubscriptionLookupPath, HttpStatusCode.NotFound, "")
            .OnJson(HttpMethod.Get, CustomerSubscriptionsPath, HttpStatusCode.OK, "[]")
            .OnJson(HttpMethod.Post, SubscriptionsPath, HttpStatusCode.Created, MaxioJson.Subscription(500, ProSubscriptionReference));

        var result = await InRequest(stub, s => s.SubscribeAsync(UserName, "eshop-pro", CancellationToken.None));

        Assert.True(result.Created);
        Assert.Equal(1, stub.Count(HttpMethod.Post, CustomersPath)); // never re-sent
        var settled = await _db.ReadAsync<BillingCustomerClaim>(CustomerReference);
        Assert.Equal(BillingClaimStatus.Created, settled!.Status);
        Assert.Equal(42, settled.ProviderId);
    }

    [Fact]
    public async Task CustomerWriteSettledInItsOwnCatchCompletesTheSubscribe()
    {
        var lookups = 0;
        var stub = NewProviderWithPlans()
            // First lookup (pre-create) finds nothing; the re-read after the failed POST finds the customer.
            .On(HttpMethod.Get, CustomerLookupPath, _ => ++lookups == 1
                ? StubHandler.Json(HttpStatusCode.NotFound, "{}")
                : StubHandler.Json(HttpStatusCode.OK, MaxioJson.Customer(42, CustomerReference)))
            .On(HttpMethod.Post, CustomersPath, _ => throw new HttpRequestException("connection reset"))
            .OnJson(HttpMethod.Get, SubscriptionLookupPath, HttpStatusCode.NotFound, "")
            .OnJson(HttpMethod.Get, CustomerSubscriptionsPath, HttpStatusCode.OK, "[]")
            .OnJson(HttpMethod.Post, SubscriptionsPath, HttpStatusCode.Created, MaxioJson.Subscription(500, ProSubscriptionReference));

        var result = await InRequest(stub, s => s.SubscribeAsync(UserName, "eshop-pro", CancellationToken.None));

        Assert.True(result.Created);
        Assert.Equal(BillingClaimStatus.Created, (await _db.ReadAsync<BillingCustomerClaim>(CustomerReference))!.Status);
    }

    [Fact]
    public async Task SubscriptionWriteSettledInItsOwnCatchReturnsTheFoundSubscription()
    {
        var lookups = 0;
        var stub = NewProviderWithPlans()
            .OnJson(HttpMethod.Get, CustomerLookupPath, HttpStatusCode.OK, MaxioJson.Customer(42, CustomerReference))
            .On(HttpMethod.Get, SubscriptionLookupPath, _ => ++lookups == 1
                ? StubHandler.Json(HttpStatusCode.NotFound, "")
                : StubHandler.Json(HttpStatusCode.OK, MaxioJson.Subscription(500, ProSubscriptionReference)))
            .OnJson(HttpMethod.Get, CustomerSubscriptionsPath, HttpStatusCode.OK, "[]")
            .On(HttpMethod.Post, SubscriptionsPath, _ => throw new HttpRequestException("connection reset"));

        var result = await InRequest(stub, s => s.SubscribeAsync(UserName, "eshop-pro", CancellationToken.None));

        Assert.True(result.Created);
        Assert.Equal(500, result.Subscription.Id);
        Assert.Equal(1, stub.Count(HttpMethod.Post, SubscriptionsPath));
        var claim = await _db.ReadAsync<BillingSubscriptionClaim>(ProSubscriptionReference);
        Assert.Equal(BillingClaimStatus.Created, claim!.Status);
        Assert.Equal(500, claim.ProviderId);
    }

    [Fact]
    public async Task UnsettledSubscriptionWriteIsRecordedUnknownAndTheNextRequestSettlesBeforeWriting()
    {
        var subscriptionReadsFail = false;
        var stub = NewProviderWithPlans()
            .OnJson(HttpMethod.Get, CustomerLookupPath, HttpStatusCode.OK, MaxioJson.Customer(42, CustomerReference))
            .On(HttpMethod.Get, SubscriptionLookupPath, _ => subscriptionReadsFail
                ? throw new HttpRequestException("connection reset")
                : StubHandler.Json(HttpStatusCode.NotFound, ""))
            .OnJson(HttpMethod.Get, CustomerSubscriptionsPath, HttpStatusCode.OK, "[]")
            .On(HttpMethod.Post, SubscriptionsPath, _ =>
            {
                subscriptionReadsFail = true; // the connection drops on the POST and stays down for the re-read
                throw new HttpRequestException("connection reset");
            });
        var shortSettle = new MaxioGatewayLimits { SettleBudget = TimeSpan.FromMilliseconds(300) };

        var ex = await Assert.ThrowsAsync<BillingOperationPendingException>(() =>
            InRequest(stub, s => s.SubscribeAsync(UserName, "eshop-pro", CancellationToken.None), shortSettle));

        Assert.Equal(BillingPendingKind.OutcomeUnknown, ex.Kind);
        Assert.Equal(ProSubscriptionReference, ex.Reference);
        Assert.Equal(BillingClaimStatus.Unknown, (await _db.ReadAsync<BillingSubscriptionClaim>(ProSubscriptionReference))!.Status);
        Assert.Contains(stub.Requests, r => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath == SubscriptionLookupPath
            && stub.Requests.IndexOf(r) > stub.Requests.FindIndex(p => p.Method == HttpMethod.Post && p.RequestUri!.AbsolutePath == SubscriptionsPath));

        // Later the provider shows the subscription: the next POST settles it and never calls CreateSubscription again.
        subscriptionReadsFail = false;
        stub.OnJson(HttpMethod.Get, SubscriptionLookupPath, HttpStatusCode.OK, MaxioJson.Subscription(500, ProSubscriptionReference));

        var result = await InRequest(stub, s => s.SubscribeAsync(UserName, "eshop-pro", CancellationToken.None));

        Assert.Equal(500, result.Subscription.Id);
        Assert.Equal(1, stub.Count(HttpMethod.Post, SubscriptionsPath));
        Assert.Equal(BillingClaimStatus.Created, (await _db.ReadAsync<BillingSubscriptionClaim>(ProSubscriptionReference))!.Status);
    }

    [Fact]
    public async Task UnknownCustomerWriteWhoseReReadAlsoFailsStaysUnknown()
    {
        var customerReadsFail = false;
        var stub = NewProviderWithPlans()
            .On(HttpMethod.Get, CustomerLookupPath, _ => customerReadsFail
                ? throw new HttpRequestException("connection reset")
                : StubHandler.Json(HttpStatusCode.NotFound, "{}"))
            .On(HttpMethod.Post, CustomersPath, _ =>
            {
                customerReadsFail = true;
                throw new HttpRequestException("connection reset");
            });
        var shortSettle = new MaxioGatewayLimits { SettleBudget = TimeSpan.FromMilliseconds(300) };

        var ex = await Assert.ThrowsAsync<BillingOperationPendingException>(() =>
            InRequest(stub, s => s.SubscribeAsync(UserName, "eshop-pro", CancellationToken.None), shortSettle));

        Assert.Equal(BillingPendingKind.OutcomeUnknown, ex.Kind);
        Assert.Equal(BillingClaimStatus.Unknown, (await _db.ReadAsync<BillingCustomerClaim>(CustomerReference))!.Status);
        Assert.Equal(1, stub.Count(HttpMethod.Post, CustomersPath));
    }

    [Fact]
    public async Task ProviderRefusalReleasesTheClaimAndSurfacesProviderMessages()
    {
        var stub = NewProviderWithPlans()
            .OnJson(HttpMethod.Get, CustomerLookupPath, HttpStatusCode.OK, MaxioJson.Customer(42, CustomerReference))
            .OnJson(HttpMethod.Get, SubscriptionLookupPath, HttpStatusCode.NotFound, "")
            .OnJson(HttpMethod.Get, CustomerSubscriptionsPath, HttpStatusCode.OK, "[]")
            .OnJson(HttpMethod.Post, SubscriptionsPath, HttpStatusCode.UnprocessableEntity, """{"errors":["A payment method is required"]}""");

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() =>
            InRequest(stub, s => s.SubscribeAsync(UserName, "eshop-pro", CancellationToken.None)));

        Assert.Equal(BillingFailureKind.Rejected, ex.Kind);
        Assert.Contains("A payment method is required", ex.ProviderMessages);
        Assert.Null(await _db.ReadAsync<BillingSubscriptionClaim>(ProSubscriptionReference));
    }

    [Fact]
    public async Task Provider422OnCreateSubscriptionReleasesTheClaimSoTheNextAttemptReachesTheProviderAgain()
    {
        // The live sandbox refusal seen with automatic collection.
        var stub = NewProviderWithPlans()
            .OnJson(HttpMethod.Get, CustomerLookupPath, HttpStatusCode.OK, MaxioJson.Customer(42, CustomerReference))
            .OnJson(HttpMethod.Get, SubscriptionLookupPath, HttpStatusCode.NotFound, "")
            .OnJson(HttpMethod.Get, CustomerSubscriptionsPath, HttpStatusCode.OK, "[]")
            .OnJson(HttpMethod.Post, SubscriptionsPath, HttpStatusCode.UnprocessableEntity,
                """{"errors":["No payment method was on file for the $299.00 balance"]}""");

        var refused = await Assert.ThrowsAsync<BillingProviderException>(() =>
            InRequest(stub, s => s.SubscribeAsync(UserName, "eshop-pro", CancellationToken.None)));
        Assert.Equal(BillingFailureKind.Rejected, refused.Kind);
        Assert.Null(await _db.ReadAsync<BillingSubscriptionClaim>(ProSubscriptionReference));

        // A repeat POST is refused by the provider again, not by a leftover claim (no 409 in_progress).
        await Assert.ThrowsAsync<BillingProviderException>(() =>
            InRequest(stub, s => s.SubscribeAsync(UserName, "eshop-pro", CancellationToken.None)));
        Assert.Equal(2, stub.Count(HttpMethod.Post, SubscriptionsPath));
        Assert.Null(await _db.ReadAsync<BillingSubscriptionClaim>(ProSubscriptionReference));

        // Once the provider accepts, the corrected attempt goes through and the claim is recorded.
        stub.OnJson(HttpMethod.Post, SubscriptionsPath, HttpStatusCode.Created, MaxioJson.Subscription(500, ProSubscriptionReference));
        var created = await InRequest(stub, s => s.SubscribeAsync(UserName, "eshop-pro", CancellationToken.None));

        Assert.True(created.Created);
        Assert.Equal(500, created.Subscription.Id);
        Assert.Equal("remittance", created.Subscription.PaymentCollectionMethod);
        Assert.Equal(3, stub.Count(HttpMethod.Post, SubscriptionsPath));
        var claim = await _db.ReadAsync<BillingSubscriptionClaim>(ProSubscriptionReference);
        Assert.Equal(BillingClaimStatus.Created, claim!.Status);
        Assert.Equal(500, claim.ProviderId);
    }

    [Fact]
    public async Task MySubscriptionsIsEmptyWhenTheShopperHasNoCustomer()
    {
        var stub = new StubHandler().OnJson(HttpMethod.Get, CustomerLookupPath, HttpStatusCode.NotFound, "{}");

        var list = await InRequest(stub, s => s.ListMySubscriptionsAsync(UserName, CancellationToken.None));

        Assert.Empty(list);
    }

    [Fact]
    public async Task MySubscriptionsListsTheCallersOwnCustomer()
    {
        var stub = new StubHandler()
            .OnJson(HttpMethod.Get, CustomerLookupPath, HttpStatusCode.OK, MaxioJson.Customer(42, CustomerReference))
            .OnJson(HttpMethod.Get, CustomerSubscriptionsPath, HttpStatusCode.OK,
                MaxioJson.List(MaxioJson.Subscription(500, ProSubscriptionReference), MaxioJson.Subscription(501, "other", "basic-plan", "canceled")));

        var list = await InRequest(stub, s => s.ListMySubscriptionsAsync(UserName, CancellationToken.None));

        Assert.Equal(new[] { 500, 501 }, list.Select(s => s.Id));
        Assert.Equal("canceled", list[1].State);
    }
}
