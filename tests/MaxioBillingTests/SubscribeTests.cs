using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.MaxioBillingTests;

public class SubscribeTests
{
    private static readonly string ProReference = MaxioSubscriptionService.ReferenceFor(Wire.UserName, "eshop-pro");

    private static Task<SubscribeOutcome> Subscribe(Harness harness, string? handle = "eshop-pro")
    {
        var db = harness.NewContext();
        return harness.NewSubscriptionService(db).SubscribeAsync(Wire.UserName, Wire.UserName, handle, Guid.NewGuid(), CancellationToken.None);
    }

    [Fact]
    public async Task Subscribe_creates_customer_and_subscription_and_records_both()
    {
        var harness = new Harness(new FakeMaxio().CreateHandler());

        var outcome = await Subscribe(harness);

        Assert.Equal(SubscribeOutcomeKind.Created, outcome.Kind);
        Assert.Equal(1001, outcome.Subscription!.Id);
        Assert.Equal("active", outcome.Subscription.State);
        Assert.Equal(29900, outcome.Subscription.PriceInCents);
        Assert.Equal("eshop-pro", outcome.Subscription.ProductHandle);
        Assert.NotNull(outcome.Subscription.NextBillingAt);

        Assert.Equal(1, harness.Stub.Count(HttpMethod.Post, "/customers.json"));
        Assert.Equal(1, harness.Stub.Count(HttpMethod.Post, "/subscriptions.json"));

        var customerBody = harness.Stub.Requests.Single(r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath == "/customers.json").Body!;
        Assert.Contains("\"reference\":\"demouser@microsoft.com\"", customerBody);
        Assert.Contains("\"email\":\"demouser@microsoft.com\"", customerBody);
        var subscriptionBody = harness.Stub.Requests.Single(r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath == "/subscriptions.json").Body!;
        Assert.Contains("\"product_handle\":\"eshop-pro\"", subscriptionBody);
        Assert.Contains("\"customer_id\":42", subscriptionBody);
        Assert.Contains($"\"reference\":\"{ProReference}\"", subscriptionBody);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", subscriptionBody);

        using var db = harness.NewContext();
        var link = await db.MaxioCustomerLinks.SingleAsync();
        Assert.Equal(BillingClaimStatus.Completed, link.Status);
        Assert.Equal(42, link.MaxioCustomerId);
        var claim = await db.MaxioSubscriptionClaims.SingleAsync();
        Assert.Equal(BillingClaimStatus.Completed, claim.Status);
        Assert.Equal(1001, claim.MaxioSubscriptionId);
    }

    [Fact]
    public async Task A_created_subscription_without_a_product_falls_back_to_the_offered_plan()
    {
        var fake = new FakeMaxio
        {
            // Subscription.Product may be null (new Catalog experience): no "product" object and no price.
            CreateSubscription = r => Task.FromResult(StubHandler.Json(HttpStatusCode.Created,
                "{\"subscription\":{\"id\":1002,\"state\":\"active\",\"reference\":\"" + FakeMaxio.SentSubscriptionReference(r) +
                "\",\"currency\":\"USD\",\"next_assessment_at\":\"2026-11-20T10:00:00-04:00\",\"customer\":{\"id\":42}}}")),
        };
        var harness = new Harness(fake.CreateHandler());

        var outcome = await Subscribe(harness);

        Assert.Equal(SubscribeOutcomeKind.Created, outcome.Kind);
        var s = outcome.Subscription!;
        Assert.Equal(1002, s.Id);
        Assert.Equal("active", s.State);
        Assert.Equal("eshop-pro", s.ProductHandle);
        Assert.Equal("Plan eshop-pro", s.ProductName);
        Assert.Equal(29900, s.PriceInCents);
        Assert.Equal(1, s.Interval);
        Assert.Equal("month", s.IntervalUnit);
        Assert.Equal(DateTimeOffset.Parse("2026-11-20T10:00:00-04:00"), s.NextBillingAt);
    }

    [Fact]
    public async Task Subscribing_twice_returns_the_existing_subscription_without_a_second_create()
    {
        var fake = new FakeMaxio();
        var harness = new Harness(fake.CreateHandler());
        await Subscribe(harness);
        fake.SubscriptionLookup = _ => Task.FromResult(StubHandler.Json(HttpStatusCode.OK, Wire.Subscription(1001, ProReference)));

        var second = await Subscribe(harness);

        Assert.Equal(SubscribeOutcomeKind.Existing, second.Kind);
        Assert.Equal(1001, second.Subscription!.Id);
        Assert.Equal(1, harness.Stub.Count(HttpMethod.Post, "/customers.json"));
        Assert.Equal(1, harness.Stub.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task A_concurrent_second_subscribe_is_refused_by_the_claim_before_it_reaches_Maxio()
    {
        var fake = new FakeMaxio();
        var releaseFirstCreate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCreateArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.CreateSubscription = async r =>
        {
            firstCreateArrived.TrySetResult();
            await releaseFirstCreate.Task;
            return StubHandler.Json(HttpStatusCode.Created, Wire.Subscription(1001, FakeMaxio.SentSubscriptionReference(r)));
        };
        var harness = new Harness(fake.CreateHandler());

        var first = Subscribe(harness);
        await firstCreateArrived.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // The double-click: the first request holds the claim and is in flight at Maxio.
        await Assert.ThrowsAsync<DuplicateException>(() => Subscribe(harness));

        releaseFirstCreate.SetResult();
        Assert.Equal(SubscribeOutcomeKind.Created, (await first).Kind);
        Assert.Equal(1, harness.Stub.Count(HttpMethod.Post, "/subscriptions.json"));
        Assert.Equal(1, harness.Stub.Count(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public async Task A_fresh_pending_claim_refuses_the_second_caller_and_sends_nothing()
    {
        var harness = new Harness(new FakeMaxio().CreateHandler());
        using (var db = harness.NewContext())
        {
            db.MaxioSubscriptionClaims.Add(new MaxioSubscriptionClaim(Wire.UserName, "eshop-pro", ProReference, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<DuplicateException>(() => Subscribe(harness));

        Assert.Equal(0, harness.Stub.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task The_in_memory_store_refuses_a_duplicate_claim_key_with_ArgumentException()
    {
        // Pins the exception the in-memory provider raises on a duplicate primary key (BillingClaimStore relies on it;
        // SQL Server raises DbUpdateException instead).
        var harness = new Harness(new FakeMaxio().CreateHandler());
        using (var first = harness.NewContext())
        {
            first.MaxioCustomerLinks.Add(new MaxioCustomerLink(Wire.UserName, DateTimeOffset.UtcNow));
            await first.SaveChangesAsync();
        }

        using var second = harness.NewContext();
        second.MaxioCustomerLinks.Add(new MaxioCustomerLink(Wire.UserName, DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<ArgumentException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task A_stale_pending_claim_is_taken_over_and_settled_by_reference_without_a_create()
    {
        var fake = new FakeMaxio
        {
            CustomerLookup = _ => Task.FromResult(StubHandler.Json(HttpStatusCode.OK, Wire.Customer(42))),
            SubscriptionLookup = _ => Task.FromResult(StubHandler.Json(HttpStatusCode.OK, Wire.Subscription(1001, ProReference))),
        };
        var harness = new Harness(fake.CreateHandler());
        using (var db = harness.NewContext())
        {
            db.MaxioSubscriptionClaims.Add(new MaxioSubscriptionClaim(Wire.UserName, "eshop-pro", ProReference, DateTimeOffset.UtcNow.AddMinutes(-10)));
            await db.SaveChangesAsync();
        }

        var outcome = await Subscribe(harness);

        Assert.Equal(SubscribeOutcomeKind.Existing, outcome.Kind);
        Assert.Equal(0, harness.Stub.Requests.Count(r => r.Method == HttpMethod.Post));
        using var check = harness.NewContext();
        Assert.Equal(BillingClaimStatus.Completed, (await check.MaxioSubscriptionClaims.SingleAsync()).Status);
    }

    [Fact]
    public async Task A_plan_outside_the_family_is_refused_before_any_claim_or_write()
    {
        var harness = new Harness(new FakeMaxio().CreateHandler());

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(() => Subscribe(harness, "not-offered"));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(0, harness.Stub.Requests.Count(r => r.Method == HttpMethod.Post));
        using var db = harness.NewContext();
        Assert.Empty(db.MaxioSubscriptionClaims);
    }

    [Fact]
    public async Task An_existing_customer_is_adopted_instead_of_created()
    {
        var fake = new FakeMaxio { CustomerLookup = _ => Task.FromResult(StubHandler.Json(HttpStatusCode.OK, Wire.Customer(77))) };
        var harness = new Harness(fake.CreateHandler());

        var outcome = await Subscribe(harness);

        Assert.Equal(SubscribeOutcomeKind.Created, outcome.Kind);
        Assert.Equal(0, harness.Stub.Count(HttpMethod.Post, "/customers.json"));
        Assert.Contains("\"customer_id\":77", harness.Stub.Requests.Single(r => r.Method == HttpMethod.Post).Body);
    }

    [Fact]
    public async Task A_422_from_CreateSubscription_is_returned_with_its_messages_and_releases_the_claim()
    {
        var fake = new FakeMaxio
        {
            CreateSubscription = _ => Task.FromResult(StubHandler.Json(HttpStatusCode.UnprocessableEntity, Wire.Errors("A payment profile is required."))),
        };
        var harness = new Harness(fake.CreateHandler());

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(() => Subscribe(harness));

        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("A payment profile is required.", ex.ProviderErrors);
        using var db = harness.NewContext();
        Assert.Empty(db.MaxioSubscriptionClaims);

        // Released: a later attempt is not blocked by a stale claim.
        fake.CreateSubscription = r => Task.FromResult(StubHandler.Json(HttpStatusCode.Created, Wire.Subscription(1001, FakeMaxio.SentSubscriptionReference(r))));
        Assert.Equal(SubscribeOutcomeKind.Created, (await Subscribe(harness)).Kind);
    }

    [Fact]
    public async Task My_subscriptions_is_empty_and_creates_nothing_when_the_user_has_no_customer()
    {
        var harness = new Harness(new FakeMaxio().CreateHandler());
        using var db = harness.NewContext();

        var items = await harness.NewSubscriptionService(db).ListMySubscriptionsAsync(Wire.UserName, Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(items);
        Assert.Equal(0, harness.Stub.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task My_subscriptions_lists_the_linked_customers_subscriptions()
    {
        var fake = new FakeMaxio
        {
            CustomerSubscriptions = _ => Task.FromResult(StubHandler.Json(HttpStatusCode.OK, "[" + Wire.Subscription(1001, ProReference) + "]")),
        };
        var harness = new Harness(fake.CreateHandler());
        await Subscribe(harness);
        using var db = harness.NewContext();

        var items = await harness.NewSubscriptionService(db).ListMySubscriptionsAsync(Wire.UserName, Guid.NewGuid(), CancellationToken.None);

        var item = Assert.Single(items);
        Assert.Equal("eshop-pro", item.ProductHandle);
        Assert.Equal(1, harness.Stub.Count(HttpMethod.Get, "/customers/42/subscriptions.json"));
    }
}
