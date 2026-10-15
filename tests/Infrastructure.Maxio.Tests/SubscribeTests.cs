using System.Net;
using System.Text.Json.Nodes;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Tests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Tests;

public class SubscribeTests
{
    private const string Buyer = "shopper@example.com";
    private const string CreateCustomer = "POST /customers.json";
    private const string CreateSubscription = "POST /subscriptions.json";
    private const string FindSubscription = "GET /subscriptions/lookup.json";

    private static SubscribeCommand Subscribe(string plan = "eshop-pro") => new(Buyer, Buyer, null, null, plan);

    [Fact]
    public async Task NewBuyerGetsACustomerAndASubscription()
    {
        var harness = new BillingHarness();

        var result = await harness.NewService().SubscribeAsync(Subscribe());

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.NotNull(result.Subscription);
        Assert.Equal("eshop-pro", result.Subscription!.PlanHandle);
        Assert.Equal(29900, result.Subscription.PriceInCents);
        Assert.Equal("active", result.Subscription.State);
        Assert.NotNull(result.Subscription.NextBillingAt);
        Assert.Equal(1, harness.Maxio.Count(CreateCustomer));
        Assert.Equal(1, harness.Maxio.Count(CreateSubscription));

        var enrollment = Assert.Single(await harness.NewStore().ListEnrollmentsAsync(Buyer));
        Assert.Equal(EnrollmentStatus.Active, enrollment.Status);
        Assert.Equal(result.Subscription.SubscriptionId, enrollment.BillingSubscriptionId);
    }

    [Fact]
    public async Task CreateRequestsCarryTheContractFields()
    {
        var harness = new BillingHarness();

        var result = await harness.NewService().SubscribeAsync(Subscribe());

        var customerBody = JsonNode.Parse(harness.Maxio.Bodies.Single(b => b.Call == CreateCustomer).Body)!["customer"]!;
        Assert.Equal(Buyer, (string?)customerBody["email"]);
        Assert.Equal("shopper", (string?)customerBody["first_name"]);
        Assert.Equal(MaxioSubscriptionBillingService.CustomerReferenceFor(Buyer), (string?)customerBody["reference"]);
        Assert.DoesNotContain("shopper", (string)customerBody["reference"]!);

        var subscriptionBody = JsonNode.Parse(harness.Maxio.Bodies.Single(b => b.Call == CreateSubscription).Body)!["subscription"]!;
        Assert.Equal("eshop-pro", (string?)subscriptionBody["product_handle"]);
        Assert.Equal(result.SubscriptionReference, (string?)subscriptionBody["reference"]);
        Assert.Equal("remittance", (string?)subscriptionBody["payment_collection_method"]);
        Assert.NotNull(subscriptionBody["customer_id"]);
    }

    [Fact]
    public async Task SubscribingAgainReturnsTheExistingSubscription()
    {
        var harness = new BillingHarness();
        var first = await harness.NewService().SubscribeAsync(Subscribe());

        var second = await harness.NewService().SubscribeAsync(Subscribe());

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, second.Outcome);
        Assert.Equal(first.Subscription!.SubscriptionId, second.Subscription!.SubscriptionId);
        Assert.Equal(1, harness.Maxio.Count(CreateSubscription));
        Assert.Equal(1, harness.Maxio.Count(CreateCustomer));
    }

    [Fact]
    public async Task ADoubleClickIsRefusedByTheClaimBeforeReachingMaxio()
    {
        var harness = new BillingHarness();
        var reachedProvider = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        harness.Maxio.Intercept = (_, call, _) =>
        {
            if (call == CreateSubscription)
            {
                reachedProvider.TrySetResult();
                release.Task.Wait(TimeSpan.FromSeconds(10));
            }
            return null;
        };

        var first = Task.Run(() => harness.NewService().SubscribeAsync(Subscribe()));
        await reachedProvider.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var ex = await Assert.ThrowsAsync<BillingException>(() => harness.NewService().SubscribeAsync(Subscribe()));
        release.SetResult();

        Assert.Equal(BillingErrorKind.Conflict, ex.Kind);
        Assert.Equal(SubscribeOutcome.Created, (await first).Outcome);
        Assert.Equal(1, harness.Maxio.Count(CreateSubscription));
    }

    [Fact]
    public async Task APlanTheFamilyDoesNotOfferIsRefusedWithoutWrites()
    {
        var harness = new BillingHarness();

        var ex = await Assert.ThrowsAsync<BillingException>(() => harness.NewService().SubscribeAsync(Subscribe("someone-elses-plan")));

        Assert.Equal(BillingErrorKind.InvalidRequest, ex.Kind);
        Assert.Equal(0, harness.Maxio.Count(CreateCustomer));
        Assert.Equal(0, harness.Maxio.Count(CreateSubscription));
    }

    [Fact]
    public async Task APlanRequiringACardIsRefused()
    {
        var maxio = FakeMaxio.WithDefaultCatalog();
        maxio.AddProduct("card-plan", "Card Plan", 1000, requireCard: true);
        var harness = new BillingHarness(maxio);

        var ex = await Assert.ThrowsAsync<BillingException>(() => harness.NewService().SubscribeAsync(Subscribe("card-plan")));

        Assert.Equal(BillingErrorKind.InvalidRequest, ex.Kind);
        Assert.Equal(0, maxio.Count(CreateSubscription));
    }

    [Fact]
    public async Task ALiveSubscriptionAlreadyAtMaxioIsAdoptedNotDuplicated()
    {
        // The local claim store was lost (e.g. in-memory database restarted) but Maxio still holds the subscription.
        var harness = new BillingHarness();
        var customer = harness.Maxio.AddCustomer(MaxioSubscriptionBillingService.CustomerReferenceFor(Buyer));
        var existing = harness.Maxio.AddSubscription((int)customer["id"]!, "eshop-pro", "eshop-sub-earlier");

        var result = await harness.NewService().SubscribeAsync(Subscribe());

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, result.Outcome);
        Assert.Equal((int)existing["id"]!, result.Subscription!.SubscriptionId);
        Assert.Equal(0, harness.Maxio.Count(CreateCustomer));
        Assert.Equal(0, harness.Maxio.Count(CreateSubscription));
    }

    [Fact]
    public async Task ACanceledSubscriptionDoesNotBlockSubscribingAgain()
    {
        var harness = new BillingHarness();
        var customer = harness.Maxio.AddCustomer(MaxioSubscriptionBillingService.CustomerReferenceFor(Buyer));
        harness.Maxio.AddSubscription((int)customer["id"]!, "eshop-pro", "eshop-sub-old", state: "canceled");

        var result = await harness.NewService().SubscribeAsync(Subscribe());

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(1, harness.Maxio.Count(CreateSubscription));
    }

    [Fact]
    public async Task AValidationRejectionReleasesTheClaimAndCarriesMaxiosMessage()
    {
        var harness = new BillingHarness();
        harness.Maxio.Intercept = (_, call, _) => call == CreateSubscription
            ? FakeMaxio.Json(HttpStatusCode.UnprocessableEntity, new JsonObject { ["errors"] = new JsonArray("No payment method was on file") })
            : null;

        var ex = await Assert.ThrowsAsync<BillingException>(() => harness.NewService().SubscribeAsync(Subscribe()));

        Assert.Equal(BillingErrorKind.Rejected, ex.Kind);
        Assert.Equal(422, ex.ProviderStatusCode);
        Assert.Contains("No payment method was on file", ex.Message);
        Assert.Empty(await harness.NewStore().ListEnrollmentsAsync(Buyer));

        harness.Maxio.Intercept = null;
        var retry = await harness.NewService().SubscribeAsync(Subscribe());
        Assert.Equal(SubscribeOutcome.Created, retry.Outcome);
    }

    [Fact]
    public async Task ConnectionLostAfterMaxioCreatedTheSubscriptionIsSettledAsCreated()
    {
        var harness = new BillingHarness();
        harness.Maxio.Intercept = (request, call, body) =>
        {
            if (call == CreateSubscription)
            {
                harness.Maxio.Apply(request, body); // Maxio acted...
                throw new HttpRequestException("connection reset"); // ...but the answer never arrived.
            }
            return null;
        };

        var result = await harness.NewService().SubscribeAsync(Subscribe());

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(1, harness.Maxio.Count(CreateSubscription)); // the SDK never resent the POST
        Assert.Equal(1, harness.Maxio.Count(FindSubscription));   // settled by the reference we sent
        Assert.Single(harness.Maxio.Subscriptions);
        var enrollment = Assert.Single(await harness.NewStore().ListEnrollmentsAsync(Buyer));
        Assert.Equal(EnrollmentStatus.Active, enrollment.Status);
    }

    [Fact]
    public async Task ConnectionLostBeforeMaxioActedReleasesTheClaim()
    {
        var harness = new BillingHarness();
        harness.Maxio.Intercept = (_, call, _) => call == CreateSubscription ? throw new HttpRequestException("connection refused") : null;

        var ex = await Assert.ThrowsAsync<BillingException>(() => harness.NewService().SubscribeAsync(Subscribe()));

        Assert.Equal(BillingErrorKind.Unavailable, ex.Kind);
        Assert.Equal(1, harness.Maxio.Count(FindSubscription));
        Assert.Empty(harness.Maxio.Subscriptions);
        Assert.Empty(await harness.NewStore().ListEnrollmentsAsync(Buyer));
    }

    [Fact]
    public async Task AnUnsettledOutcomeIsRecordedAndSettledByTheNextRead()
    {
        var harness = new BillingHarness();
        harness.Maxio.Intercept = (request, call, body) =>
        {
            if (call == CreateSubscription)
            {
                harness.Maxio.Apply(request, body);
                throw new HttpRequestException("connection reset");
            }
            if (call == FindSubscription)
            {
                throw new HttpRequestException("still down");
            }
            return null;
        };

        var result = await harness.NewService().SubscribeAsync(Subscribe());

        Assert.Equal(SubscribeOutcome.PendingConfirmation, result.Outcome);
        Assert.Null(result.Subscription);
        Assert.Equal(EnrollmentStatus.Unknown, Assert.Single(await harness.NewStore().ListEnrollmentsAsync(Buyer)).Status);

        harness.Maxio.Intercept = null;
        var subscriptions = await harness.NewService().ListSubscriptionsAsync(Buyer);

        var settled = Assert.Single(await harness.NewStore().ListEnrollmentsAsync(Buyer));
        Assert.Equal(EnrollmentStatus.Active, settled.Status);
        Assert.Equal(settled.BillingSubscriptionId, Assert.Single(subscriptions).SubscriptionId);
        Assert.Equal(1, harness.Maxio.Count(CreateSubscription));
    }

    [Fact]
    public async Task AStaleClaimWhoseAttemptNeverLandedIsTakenOver()
    {
        var harness = new BillingHarness();
        var stale = new SubscriptionEnrollment(Buyer, "eshop-pro", "eshop-sub-lost", harness.Clock.GetUtcNow().AddMinutes(-30));
        Assert.Null(await harness.NewStore().TryClaimEnrollmentAsync(stale));

        var result = await harness.NewService().SubscribeAsync(Subscribe());

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.NotEqual("eshop-sub-lost", result.SubscriptionReference);
        Assert.Equal(1, harness.Maxio.Count(CreateSubscription));
    }

    [Fact]
    public async Task ConnectionLostAfterMaxioCreatedTheCustomerIsSettledByReference()
    {
        var harness = new BillingHarness();
        harness.Maxio.Intercept = (request, call, body) =>
        {
            if (call == CreateCustomer)
            {
                harness.Maxio.Apply(request, body);
                throw new HttpRequestException("connection reset");
            }
            return null;
        };

        var result = await harness.NewService().SubscribeAsync(Subscribe());

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(1, harness.Maxio.Count(CreateCustomer));
        Assert.Single(harness.Maxio.Customers);
        Assert.True((await harness.NewStore().GetCustomerAsync(Buyer))!.IsLinked);
    }

    [Fact]
    public async Task ATakenCustomerReferenceIsResolvedToTheExistingCustomer()
    {
        // Another instance created the customer between our lookup and our create.
        var harness = new BillingHarness();
        var lookups = 0;
        harness.Maxio.Intercept = (_, call, _) =>
        {
            if (call == "GET /customers/lookup.json" && Interlocked.Increment(ref lookups) == 1)
            {
                harness.Maxio.AddCustomer(MaxioSubscriptionBillingService.CustomerReferenceFor(Buyer));
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            return null;
        };

        var result = await harness.NewService().SubscribeAsync(Subscribe());

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Single(harness.Maxio.Customers);
    }

    [Fact]
    public async Task MySubscriptionsIsEmptyForABuyerWithoutABillingAccount()
    {
        var harness = new BillingHarness();

        var subscriptions = await harness.NewService().ListSubscriptionsAsync(Buyer);

        Assert.Empty(subscriptions);
        Assert.Equal(0, harness.Maxio.Count(CreateCustomer));
    }

    [Fact]
    public async Task MySubscriptionsComesFromMaxio()
    {
        var harness = new BillingHarness();
        await harness.NewService().SubscribeAsync(Subscribe("eshop-pro"));
        await harness.NewService().SubscribeAsync(Subscribe("basic-plan"));

        var subscriptions = await harness.NewService().ListSubscriptionsAsync(Buyer);

        Assert.Equal(new[] { "basic-plan", "eshop-pro" }, subscriptions.Select(s => s.PlanHandle).OrderBy(h => h));
        Assert.Equal(1, harness.Maxio.Count(CreateCustomer));
    }
}
