using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.MaxioBillingTests;

/// <summary>
/// The connection fails after the provider may have acted. The code must settle the outcome by re-reading,
/// never report a failure, and never send the write twice.
/// </summary>
public class UnknownOutcomeTests
{
    private static readonly string ProReference = MaxioSubscriptionService.ReferenceFor(Wire.UserName, "eshop-pro");

    private static Task<SubscribeOutcome> Subscribe(Harness harness)
    {
        var db = harness.NewContext();
        return harness.NewSubscriptionService(db).SubscribeAsync(Wire.UserName, Wire.UserName, "eshop-pro", Guid.NewGuid(), CancellationToken.None);
    }

    private static Task<CustomerOutcome> EnsureCustomer(Harness harness)
    {
        var db = harness.NewContext();
        return harness.NewCustomerService(db).EnsureCustomerAsync(Wire.UserName, Wire.UserName, CancellationToken.None);
    }

    // ------------------------------------------------------------- CreateCustomer

    [Fact]
    public async Task CreateCustomer_connection_failure_is_settled_by_reading_the_customer_by_reference()
    {
        var fake = new FakeMaxio
        {
            CustomerLookup = FakeMaxio.Sequence(
                _ => StubHandler.Empty(HttpStatusCode.NotFound),               // the pre-create lookup
                _ => StubHandler.Json(HttpStatusCode.OK, Wire.Customer(42))), // the re-read: it was created
            CreateCustomer = FakeMaxio.ConnectionReset,
        };
        var harness = new Harness(fake.CreateHandler());

        var outcome = await EnsureCustomer(harness);

        Assert.Equal(42, outcome.MaxioCustomerId);
        Assert.Equal(1, harness.Stub.Count(HttpMethod.Post, "/customers.json"));       // never resent
        Assert.Equal(2, harness.Stub.Count(HttpMethod.Get, "/customers/lookup.json"));
        using var db = harness.NewContext();
        var link = await db.MaxioCustomerLinks.SingleAsync();
        Assert.Equal(BillingClaimStatus.Completed, link.Status);
        Assert.Equal(42, link.MaxioCustomerId);
    }

    [Fact]
    public async Task CreateCustomer_connection_failure_that_cannot_be_settled_is_recorded_unknown_and_settled_next_time()
    {
        var fake = new FakeMaxio { CreateCustomer = FakeMaxio.ConnectionReset };
        var harness = new Harness(fake.CreateHandler());

        var first = await EnsureCustomer(harness);

        Assert.True(first.IsPending);
        using (var db = harness.NewContext())
        {
            Assert.Equal(BillingClaimStatus.Unknown, (await db.MaxioCustomerLinks.SingleAsync()).Status);
        }

        // The provider had in fact created it; the next request re-reads before any new create.
        fake.CustomerLookup = _ => Task.FromResult(StubHandler.Json(HttpStatusCode.OK, Wire.Customer(42)));
        var second = await EnsureCustomer(harness);

        Assert.Equal(42, second.MaxioCustomerId);
        Assert.Equal(1, harness.Stub.Count(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public async Task CreateCustomer_422_for_an_existing_reference_adopts_the_customer()
    {
        var fake = new FakeMaxio
        {
            CustomerLookup = FakeMaxio.Sequence(
                _ => StubHandler.Empty(HttpStatusCode.NotFound),
                _ => StubHandler.Json(HttpStatusCode.OK, Wire.Customer(42))),
            CreateCustomer = _ => Task.FromResult(StubHandler.Json(HttpStatusCode.UnprocessableEntity, Wire.Errors("Reference must be unique"))),
        };
        var harness = new Harness(fake.CreateHandler());

        var outcome = await EnsureCustomer(harness);

        Assert.Equal(42, outcome.MaxioCustomerId);
    }

    // ------------------------------------------------------------- CreateSubscription

    [Fact]
    public async Task CreateSubscription_connection_failure_is_settled_by_finding_the_subscription_by_reference()
    {
        var fake = new FakeMaxio
        {
            SubscriptionLookup = FakeMaxio.Sequence(
                _ => StubHandler.Empty(HttpStatusCode.NotFound),                                   // pre-create lookup
                _ => StubHandler.Json(HttpStatusCode.OK, Wire.Subscription(1001, ProReference))), // re-read after the failure
            CreateSubscription = FakeMaxio.ConnectionReset,
        };
        var harness = new Harness(fake.CreateHandler());

        var outcome = await Subscribe(harness);

        Assert.Equal(SubscribeOutcomeKind.Created, outcome.Kind);
        Assert.Equal(1001, outcome.Subscription!.Id);
        Assert.Equal(1, harness.Stub.Count(HttpMethod.Post, "/subscriptions.json")); // never resent
        var reRead = harness.Stub.Requests.Last(r => r.Uri.AbsolutePath == "/subscriptions/lookup.json");
        Assert.Contains(ProReference, reRead.Uri.Query);
        using var db = harness.NewContext();
        var claim = await db.MaxioSubscriptionClaims.SingleAsync();
        Assert.Equal(BillingClaimStatus.Completed, claim.Status);
        Assert.Equal(1001, claim.MaxioSubscriptionId);
    }

    [Fact]
    public async Task CreateSubscription_connection_failure_falls_back_to_the_customers_subscription_list_when_the_lookup_fails()
    {
        var fake = new FakeMaxio
        {
            SubscriptionLookup = FakeMaxio.Sequence(
                _ => StubHandler.Empty(HttpStatusCode.NotFound),
                _ => StubHandler.Json(HttpStatusCode.BadRequest, Wire.Errors("lookup unavailable"))),
            CreateSubscription = FakeMaxio.ConnectionReset,
            CustomerSubscriptions = _ => Task.FromResult(StubHandler.Json(HttpStatusCode.OK, "[" + Wire.Subscription(1001, ProReference) + "]")),
        };
        var harness = new Harness(fake.CreateHandler());

        var outcome = await Subscribe(harness);

        Assert.Equal(SubscribeOutcomeKind.Created, outcome.Kind);
        Assert.Equal(1001, outcome.Subscription!.Id);
        Assert.Equal(1, harness.Stub.Count(HttpMethod.Get, "/customers/42/subscriptions.json"));
        Assert.Equal(1, harness.Stub.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task CreateSubscription_connection_failure_that_cannot_be_settled_returns_pending_then_settles_without_a_second_create()
    {
        var fake = new FakeMaxio { CreateSubscription = FakeMaxio.ConnectionReset };
        var harness = new Harness(fake.CreateHandler());

        var first = await Subscribe(harness);

        Assert.Equal(SubscribeOutcomeKind.Pending, first.Kind);
        using (var db = harness.NewContext())
        {
            Assert.Equal(BillingClaimStatus.Unknown, (await db.MaxioSubscriptionClaims.SingleAsync()).Status);
        }

        // It had landed at Maxio. The next request takes the Unknown claim, re-reads, and adopts it.
        fake.SubscriptionLookup = _ => Task.FromResult(StubHandler.Json(HttpStatusCode.OK, Wire.Subscription(1001, ProReference)));
        var second = await Subscribe(harness);

        Assert.Equal(SubscribeOutcomeKind.Existing, second.Kind);
        Assert.Equal(1001, second.Subscription!.Id);
        Assert.Equal(1, harness.Stub.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task CreateSubscription_5xx_is_treated_as_unknown_and_settled_by_reference()
    {
        var fake = new FakeMaxio
        {
            SubscriptionLookup = FakeMaxio.Sequence(
                _ => StubHandler.Empty(HttpStatusCode.NotFound),
                _ => StubHandler.Json(HttpStatusCode.OK, Wire.Subscription(1001, ProReference))),
            CreateSubscription = _ => Task.FromResult(StubHandler.Empty(HttpStatusCode.BadGateway)),
        };
        var harness = new Harness(fake.CreateHandler());

        var outcome = await Subscribe(harness);

        Assert.Equal(SubscribeOutcomeKind.Created, outcome.Kind);
        Assert.Equal(1, harness.Stub.Count(HttpMethod.Post, "/subscriptions.json")); // POST is not retried on 502
    }

    [Fact]
    public async Task CreateSubscription_unreadable_2xx_body_is_treated_as_unknown_and_settled_by_reference()
    {
        var fake = new FakeMaxio
        {
            SubscriptionLookup = FakeMaxio.Sequence(
                _ => StubHandler.Empty(HttpStatusCode.NotFound),
                _ => StubHandler.Json(HttpStatusCode.OK, Wire.Subscription(1001, ProReference))),
            CreateSubscription = _ => Task.FromResult(StubHandler.Json(HttpStatusCode.Created, """{"subscription":"not-an-object"}""")),
        };
        var harness = new Harness(fake.CreateHandler());

        var outcome = await Subscribe(harness);

        Assert.Equal(SubscribeOutcomeKind.Created, outcome.Kind);
        Assert.Equal(1001, outcome.Subscription!.Id);
    }
}
