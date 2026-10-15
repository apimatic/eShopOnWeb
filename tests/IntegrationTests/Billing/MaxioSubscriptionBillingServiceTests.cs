using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;
using static Microsoft.eShopWeb.IntegrationTests.Billing.FakeMaxio;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private const string Plans = "/product_families/handle:test-family/products.json";
    private const string CustomerLookup = "/customers/lookup.json";
    private const string Customers = "/customers.json";
    private const string CustomerSubscriptions = "/customers/10/subscriptions.json";
    private const string Subscriptions = "/subscriptions.json";
    private const string SubscriptionLookup = "/subscriptions/lookup.json";

    private readonly string _database = Guid.NewGuid().ToString();

    /// <summary>Maxio with no customer yet for the demo user; creating one and subscribing both succeed.</summary>
    private static FakeMaxio NewCustomerMaxio() => new FakeMaxio()
        .On("GET", CustomerLookup, _ => Empty(HttpStatusCode.NotFound))
        .On("POST", Customers, _ => Json(HttpStatusCode.Created, Customer(10)))
        .On("GET", CustomerSubscriptions, _ => Json(HttpStatusCode.OK, Array.Empty<object>()))
        .On("POST", Subscriptions, _ => Json(HttpStatusCode.Created, Subscription(500, "eshop-pro")));

    [Fact]
    public async Task ListPlansReturnsOfferedProductsAndSkipsArchived()
    {
        var maxio = new FakeMaxio();

        var catalog = await maxio.CreateService(NewContext(_database)).ListPlansAsync();

        Assert.False(catalog.IsTruncated);
        Assert.Equal(new[] { "basic-plan", "eshop-pro" }, catalog.Plans.Select(p => p.Handle));
        var pro = catalog.Plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal("month", pro.IntervalUnit);
    }

    [Fact]
    public async Task ListPlansStopsAtThePageCapAndSaysTheListIsIncomplete()
    {
        var maxio = new FakeMaxio().On("GET", Plans, request =>
        {
            var page = int.Parse(HttpUtility.ParseQueryString(request.RequestUri!.Query)["page"]!);
            var products = Enumerable.Range(0, MaxioSubscriptionBillingService.PlansPerPage)
                .Select(i => Product(page * 1000 + i, $"plan-{page}-{i}", "Plan", 100)).ToArray();
            return Json(HttpStatusCode.OK, Products(products));
        });

        var catalog = await maxio.CreateService(NewContext(_database)).ListPlansAsync();

        Assert.True(catalog.IsTruncated);
        Assert.Equal(MaxioSubscriptionBillingService.MaxPlanPages, maxio.Count("GET", Plans));
        Assert.Equal(MaxioSubscriptionBillingService.MaxPlanPages * MaxioSubscriptionBillingService.PlansPerPage, catalog.Plans.Count);
    }

    [Fact]
    public async Task SubscribeCreatesCustomerAndSubscriptionWithoutPaymentMethod()
    {
        var maxio = NewCustomerMaxio();
        var db = NewContext(_database);

        var result = await maxio.CreateService(db).SubscribeAsync(DemoUser, "eshop-pro");

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(500, result.Subscription!.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(29900, result.Subscription.PriceInCents);
        Assert.NotNull(result.Subscription.NextBillingAt);

        var customerBody = maxio.LastBody("POST", Customers)!;
        Assert.Contains("\"email\":\"demouser@microsoft.com\"", customerBody);
        Assert.Contains($"\"reference\":\"{MaxioSubscriptionBillingService.CustomerReference(DemoUser)}\"", customerBody);

        var subscriptionBody = maxio.LastBody("POST", Subscriptions)!;
        Assert.Contains("\"product_handle\":\"eshop-pro\"", subscriptionBody);
        Assert.Contains("\"customer_id\":10", subscriptionBody);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", subscriptionBody);

        var enrollment = await NewContext(_database).SubscriptionEnrollments.SingleAsync();
        Assert.Equal(SubscriptionEnrollmentStatus.Active, enrollment.Status);
        Assert.Equal(10, enrollment.BillingCustomerId);
        Assert.Equal(500, enrollment.BillingSubscriptionId);
        Assert.Contains($"\"reference\":\"{enrollment.SubscriptionReference}\"", subscriptionBody);
    }

    [Fact]
    public async Task CustomerReferenceIsStablePerUserAndCarriesNoEmail()
    {
        var reference = MaxioSubscriptionBillingService.CustomerReference(DemoUser);

        Assert.Equal(reference, MaxioSubscriptionBillingService.CustomerReference(DemoUser with { UserId = "re-seeded-id" }));
        Assert.DoesNotContain("demouser", reference, StringComparison.OrdinalIgnoreCase);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task SubscribeRefusesAPlanTheFamilyDoesNotOffer()
    {
        var maxio = NewCustomerMaxio();

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => maxio.CreateService(NewContext(_database)).SubscribeAsync(DemoUser, "old-plan"));

        Assert.Equal(SubscriptionBillingErrorKind.PlanNotOffered, ex.Kind);
        Assert.DoesNotContain(maxio.Requests, r => r.Method == HttpMethod.Post);
        Assert.Empty(NewContext(_database).SubscriptionEnrollments);
    }

    [Fact]
    public async Task ConcurrentDoubleClickCreatesExactlyOneSubscription()
    {
        var maxio = NewCustomerMaxio().OnAsync("POST", Subscriptions, async _ =>
        {
            await Task.Delay(300);
            return Json(HttpStatusCode.Created, Subscription(500, "eshop-pro"));
        });

        var first = maxio.CreateService(NewContext(_database)).SubscribeAsync(DemoUser, "eshop-pro");
        var second = maxio.CreateService(NewContext(_database)).SubscribeAsync(DemoUser, "eshop-pro");
        var outcomes = await Task.WhenAll(Settle(first), Settle(second));

        Assert.Equal(1, maxio.Count("POST", Subscriptions));
        Assert.Equal(1, maxio.Count("POST", Customers));
        Assert.Single(outcomes, o => o.Result?.Outcome == SubscribeOutcome.Created);
        Assert.Single(outcomes, o => o.Error?.Kind == SubscriptionBillingErrorKind.InProgress);
    }

    [Fact]
    public async Task RepeatSubscribeReturnsTheExistingSubscription()
    {
        var maxio = NewCustomerMaxio();
        await maxio.CreateService(NewContext(_database)).SubscribeAsync(DemoUser, "eshop-pro");
        maxio.On("GET", CustomerSubscriptions, _ => Json(HttpStatusCode.OK, new[] { Subscription(500, "eshop-pro") }));

        var repeat = await maxio.CreateService(NewContext(_database)).SubscribeAsync(DemoUser, "eshop-pro");

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, repeat.Outcome);
        Assert.Equal(500, repeat.Subscription!.Id);
        Assert.Equal(1, maxio.Count("POST", Subscriptions));
    }

    [Fact]
    public async Task SubscribeToAnotherPlanWhileActiveIsAConflict()
    {
        var maxio = NewCustomerMaxio();
        await maxio.CreateService(NewContext(_database)).SubscribeAsync(DemoUser, "eshop-pro");
        maxio.On("GET", CustomerSubscriptions, _ => Json(HttpStatusCode.OK, new[] { Subscription(500, "eshop-pro") }));

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => maxio.CreateService(NewContext(_database)).SubscribeAsync(DemoUser, "basic-plan"));

        Assert.Equal(SubscriptionBillingErrorKind.AlreadySubscribed, ex.Kind);
        Assert.Equal(1, maxio.Count("POST", Subscriptions));
    }

    [Fact]
    public async Task ResubscribeIsAllowedAfterTheSubscriptionWasCanceled()
    {
        var maxio = NewCustomerMaxio();
        await maxio.CreateService(NewContext(_database)).SubscribeAsync(DemoUser, "eshop-pro");
        maxio.On("GET", CustomerSubscriptions, _ => Json(HttpStatusCode.OK, new[] { Subscription(500, "eshop-pro", "canceled") }))
            .On("POST", Subscriptions, _ => Json(HttpStatusCode.Created, Subscription(501, "basic-plan")));

        var result = await maxio.CreateService(NewContext(_database)).SubscribeAsync(DemoUser, "basic-plan");

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(501, result.Subscription!.Id);
        Assert.Equal(2, maxio.Count("POST", Subscriptions));
    }

    [Fact]
    public async Task ExistingLiveSubscriptionIsAdoptedWhenTheEnrollmentRecordWasLost()
    {
        // e.g. the in-memory store was wiped by a restart: Maxio still knows the customer and its subscription.
        var maxio = NewCustomerMaxio()
            .On("GET", CustomerLookup, _ => Json(HttpStatusCode.OK, Customer(10)))
            .On("GET", CustomerSubscriptions, _ => Json(HttpStatusCode.OK, new[] { Subscription(500, "eshop-pro") }));

        var result = await maxio.CreateService(NewContext(_database)).SubscribeAsync(DemoUser, "eshop-pro");

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, result.Outcome);
        Assert.DoesNotContain(maxio.Requests, r => r.Method == HttpMethod.Post);
        Assert.Equal(SubscriptionEnrollmentStatus.Active, (await NewContext(_database).SubscriptionEnrollments.SingleAsync()).Status);
    }

    [Fact]
    public async Task FailedConnectionOnCreateSubscriptionIsSettledByReReadingTheReference()
    {
        var maxio = NewCustomerMaxio()
            .On("POST", Subscriptions, _ => throw new HttpRequestException("connection reset"))
            .On("GET", SubscriptionLookup, request => Json(HttpStatusCode.OK,
                Subscription(500, "eshop-pro", reference: HttpUtility.ParseQueryString(request.RequestUri!.Query)["reference"])));

        var result = await maxio.CreateService(NewContext(_database)).SubscribeAsync(DemoUser, "eshop-pro");

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(500, result.Subscription!.Id);
        Assert.Equal(1, maxio.Count("POST", Subscriptions)); // never resent
        var enrollment = await NewContext(_database).SubscriptionEnrollments.SingleAsync();
        Assert.Equal(SubscriptionEnrollmentStatus.Active, enrollment.Status);
    }

    [Fact]
    public async Task UnsettledCreateIsRecordedAsUnknownAndSettledByTheNextRequest()
    {
        var maxio = NewCustomerMaxio()
            .On("POST", Subscriptions, _ => throw new HttpRequestException("connection reset"))
            .On("GET", SubscriptionLookup, _ => Empty(HttpStatusCode.NotFound));

        var first = await maxio.CreateService(NewContext(_database)).SubscribeAsync(DemoUser, "eshop-pro");

        Assert.Equal(SubscribeOutcome.Unconfirmed, first.Outcome);
        var unknown = await NewContext(_database).SubscriptionEnrollments.SingleAsync();
        Assert.Equal(SubscriptionEnrollmentStatus.Unknown, unknown.Status);

        // The create had landed after all; Maxio now finds it by the reference that was sent.
        maxio.On("GET", SubscriptionLookup, _ => Json(HttpStatusCode.OK, Subscription(500, "eshop-pro", reference: unknown.SubscriptionReference)));
        var second = await maxio.CreateService(NewContext(_database)).SubscribeAsync(DemoUser, "eshop-pro");

        Assert.Equal(SubscribeOutcome.Created, second.Outcome);
        Assert.Equal(500, second.Subscription!.Id);
        Assert.Equal(1, maxio.Count("POST", Subscriptions));
        Assert.Equal(SubscriptionEnrollmentStatus.Active, (await NewContext(_database).SubscriptionEnrollments.SingleAsync()).Status);
    }

    [Fact]
    public async Task UnknownEnrollmentIsSettledWhenTheUserListsSubscriptions()
    {
        var maxio = NewCustomerMaxio()
            .On("POST", Subscriptions, _ => throw new HttpRequestException("connection reset"))
            .On("GET", SubscriptionLookup, _ => Empty(HttpStatusCode.NotFound));
        await maxio.CreateService(NewContext(_database)).SubscribeAsync(DemoUser, "eshop-pro");

        maxio.On("GET", SubscriptionLookup, _ => Json(HttpStatusCode.OK, Subscription(500, "eshop-pro")))
            .On("GET", CustomerSubscriptions, _ => Json(HttpStatusCode.OK, new[] { Subscription(500, "eshop-pro") }));
        var mine = await maxio.CreateService(NewContext(_database)).GetSubscriptionsAsync(DemoUser);

        Assert.False(mine.HasUnconfirmedEnrollment);
        Assert.Equal(500, mine.Subscriptions.Single().Id);
        Assert.Equal(SubscriptionEnrollmentStatus.Active, (await NewContext(_database).SubscriptionEnrollments.SingleAsync()).Status);
    }

    [Fact]
    public async Task FailedConnectionOnCreateCustomerIsSettledByReReadingTheReference()
    {
        var created = false;
        var maxio = NewCustomerMaxio()
            .On("GET", CustomerLookup, _ => created ? Json(HttpStatusCode.OK, Customer(10)) : Empty(HttpStatusCode.NotFound))
            .On("POST", Customers, _ =>
            {
                created = true; // it landed, but the answer is lost
                throw new HttpRequestException("connection reset");
            });

        var result = await maxio.CreateService(NewContext(_database)).SubscribeAsync(DemoUser, "eshop-pro");

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(1, maxio.Count("POST", Customers));
        Assert.Contains("\"customer_id\":10", maxio.LastBody("POST", Subscriptions));
    }

    [Fact]
    public async Task ProviderRejectionReleasesTheClaimAndReportsTheReasons()
    {
        var maxio = NewCustomerMaxio()
            .On("POST", Subscriptions, _ => Json(HttpStatusCode.UnprocessableEntity, new { errors = new[] { "No payment method was on file" } }));

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => maxio.CreateService(NewContext(_database)).SubscribeAsync(DemoUser, "eshop-pro"));

        Assert.Equal(SubscriptionBillingErrorKind.Rejected, ex.Kind);
        Assert.Equal(new[] { "No payment method was on file" }, ex.Details);
        Assert.Empty(NewContext(_database).SubscriptionEnrollments);
    }

    [Fact]
    public async Task RefusedCredentialsAreOurProblemNotTheCallers()
    {
        var maxio = new FakeMaxio().On("GET", Plans, _ => Empty(HttpStatusCode.Unauthorized));

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => maxio.CreateService(NewContext(_database)).ListPlansAsync());

        Assert.Equal(SubscriptionBillingErrorKind.ProviderError, ex.Kind);
        Assert.DoesNotContain("maxio.test", ex.Message);
    }

    [Fact]
    public async Task UnreachableProviderIsReportedAsUnavailable()
    {
        var maxio = new FakeMaxio().On("GET", Plans, _ => throw new HttpRequestException("no route to host"));

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => maxio.CreateService(NewContext(_database)).ListPlansAsync());

        Assert.Equal(SubscriptionBillingErrorKind.Unavailable, ex.Kind);
    }

    [Fact]
    public async Task AnAbandonedPendingClaimIsTakenOverAndSettled()
    {
        using (var db = NewContext(_database))
        {
            var stale = DateTimeOffset.UtcNow - MaxioSubscriptionBillingService.StalePendingAfter - TimeSpan.FromMinutes(1);
            db.SubscriptionEnrollments.Add(new SubscriptionEnrollment(DemoUser.UserId, "eshop-pro", "eshop-sub-abandoned", stale));
            await db.SaveChangesAsync();
        }

        var maxio = NewCustomerMaxio()
            .On("GET", SubscriptionLookup, _ => Json(HttpStatusCode.OK, Subscription(500, "eshop-pro", reference: "eshop-sub-abandoned")));

        var result = await maxio.CreateService(NewContext(_database)).SubscribeAsync(DemoUser, "eshop-pro");

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(0, maxio.Count("POST", Subscriptions));
    }

    [Fact]
    public async Task AFreshPendingClaimRefusesTheSecondRequestBeforeAnyWrite()
    {
        using (var db = NewContext(_database))
        {
            db.SubscriptionEnrollments.Add(new SubscriptionEnrollment(DemoUser.UserId, "eshop-pro", "eshop-sub-inflight", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        var maxio = NewCustomerMaxio();
        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => maxio.CreateService(NewContext(_database)).SubscribeAsync(DemoUser, "eshop-pro"));

        Assert.Equal(SubscriptionBillingErrorKind.InProgress, ex.Kind);
        Assert.DoesNotContain(maxio.Requests, r => r.Method == HttpMethod.Post);
    }

    private static async Task<(SubscribeResult? Result, SubscriptionBillingException? Error)> Settle(Task<SubscribeResult> call)
    {
        try
        {
            return (await call, null);
        }
        catch (SubscriptionBillingException ex)
        {
            return (null, ex);
        }
    }
}
