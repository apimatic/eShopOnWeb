using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionBillingServiceTests
{
    private static readonly BillingCustomerIdentity Demo =
        new("demouser@microsoft.com", "demouser@microsoft.com");

    private static MaxioTestHarness CatalogHarness(bool requireCreditCard = false) =>
        new MaxioTestHarness()
            .Route(HttpMethod.Get, MaxioPayloads.ProductFamiliesPath, HttpStatusCode.OK, MaxioPayloads.ProductFamilies())
            .Route(HttpMethod.Get, MaxioPayloads.SitePath, HttpStatusCode.OK, MaxioPayloads.Site())
            .Route(HttpMethod.Get, MaxioPayloads.ProductsPath(), HttpStatusCode.OK, MaxioPayloads.Products(requireCreditCard));

    [Fact]
    public async Task GetPlansAsync_ProjectsProviderCatalogueAndDropsArchivedPlans()
    {
        var harness = CatalogHarness();

        var plans = await harness.BuildService().GetPlansAsync();

        Assert.Equal(new[] { "basic-plan", "eshop-pro" }, plans.Select(plan => plan.Handle));

        var pro = plans.Single(plan => plan.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal("USD", pro.Currency);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.False(pro.HasTrial);
        Assert.False(pro.RequiresPaymentMethod);
    }

    [Fact]
    public async Task GetPlansAsync_ResolvesTheFamilyByHandleNotByAConfiguredId()
    {
        // Provider ids are reassigned when the catalog is re-seeded, so the handle is the only stable key.
        // Proven by pointing the matching handle at an id nothing else in the harness uses.
        var harness = new MaxioTestHarness()
            .Route(HttpMethod.Get, MaxioPayloads.ProductFamiliesPath, HttpStatusCode.OK,
                MaxioPayloads.ProductFamilies(matchingId: 999123))
            .Route(HttpMethod.Get, MaxioPayloads.SitePath, HttpStatusCode.OK, MaxioPayloads.Site())
            .Route(HttpMethod.Get, MaxioPayloads.ProductsPath(999123), HttpStatusCode.OK, MaxioPayloads.Products());

        var plans = await harness.BuildService().GetPlansAsync();

        Assert.NotEmpty(plans);
        Assert.Equal(1, harness.CountOf(HttpMethod.Get, MaxioPayloads.ProductsPath(999123)));
        Assert.Contains("include_archived=false", harness.Last(HttpMethod.Get, MaxioPayloads.ProductsPath(999123)).Query);
    }

    [Fact]
    public async Task GetPlansAsync_WhenConfiguredFamilyHandleMatchesNothing_FailsLoudly()
    {
        var harness = CatalogHarness();
        harness.Settings.ProductFamilyHandle = "not-on-this-site";

        var exception = await Assert.ThrowsAsync<BillingException>(
            () => harness.BuildService().GetPlansAsync());

        Assert.Equal(BillingFailureKind.NotConfigured, exception.Kind);
    }

    [Fact]
    public async Task SubscribeAsync_WhenCustomerIsNew_CreatesCustomerThenSubscribesByHandle()
    {
        var harness = CatalogHarness()
            .RouteSequence(HttpMethod.Get, MaxioPayloads.CustomerLookupPath,
                (HttpStatusCode.NotFound, MaxioPayloads.NotFound()),
                (HttpStatusCode.OK, MaxioPayloads.Customer()))
            .Route(HttpMethod.Post, MaxioPayloads.CreateCustomerPath, HttpStatusCode.Created, MaxioPayloads.Customer())
            .Route(HttpMethod.Get, MaxioPayloads.CustomerSubscriptionsPath(), HttpStatusCode.OK, MaxioPayloads.EmptySubscriptionList)
            .Route(HttpMethod.Post, MaxioPayloads.CreateSubscriptionPath, HttpStatusCode.Created, MaxioPayloads.Subscription());

        var subscription = await harness.BuildService().SubscribeAsync(Demo, "eshop-pro");

        Assert.True(subscription.WasCreatedByThisRequest);
        Assert.Equal(94211243, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.True(subscription.IsActive);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal(MaxioTestHarness.CustomerId, subscription.CustomerId);

        // The customer is keyed on a reference derived from the user, which is what makes it findable again.
        var customerRequest = harness.Last(HttpMethod.Post, MaxioPayloads.CreateCustomerPath);
        Assert.Contains($"\"reference\":\"{MaxioTestHarness.CustomerReference}\"", customerRequest.Body);
        Assert.Contains("\"email\":\"demouser@microsoft.com\"", customerRequest.Body);

        // Subscribing addresses the plan by handle and invoices the balance, so no card is needed.
        var subscribeRequest = harness.Last(HttpMethod.Post, MaxioPayloads.CreateSubscriptionPath);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", subscribeRequest.Body);
        Assert.Contains($"\"customer_id\":{MaxioTestHarness.CustomerId}", subscribeRequest.Body);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", subscribeRequest.Body);
    }

    [Fact]
    public async Task SubscribeAsync_OnLegacyBillingSite_CollectsByInvoiceInstead()
    {
        var harness = new MaxioTestHarness()
            .Route(HttpMethod.Get, MaxioPayloads.ProductFamiliesPath, HttpStatusCode.OK, MaxioPayloads.ProductFamilies())
            .Route(HttpMethod.Get, MaxioPayloads.SitePath, HttpStatusCode.OK,
                MaxioPayloads.Site(relationshipInvoicing: false))
            .Route(HttpMethod.Get, MaxioPayloads.ProductsPath(), HttpStatusCode.OK, MaxioPayloads.Products())
            .Route(HttpMethod.Get, MaxioPayloads.CustomerLookupPath, HttpStatusCode.OK, MaxioPayloads.Customer())
            .Route(HttpMethod.Get, MaxioPayloads.CustomerSubscriptionsPath(), HttpStatusCode.OK, MaxioPayloads.EmptySubscriptionList)
            .Route(HttpMethod.Post, MaxioPayloads.CreateSubscriptionPath, HttpStatusCode.Created, MaxioPayloads.Subscription());

        await harness.BuildService().SubscribeAsync(Demo, "eshop-pro");

        Assert.Contains(
            "\"payment_collection_method\":\"invoice\"",
            harness.Last(HttpMethod.Post, MaxioPayloads.CreateSubscriptionPath).Body);
    }

    [Fact]
    public async Task SubscribeAsync_WhenAlreadySubscribed_ReturnsExistingWithoutEnrollingAgain()
    {
        var harness = CatalogHarness()
            .Route(HttpMethod.Get, MaxioPayloads.CustomerLookupPath, HttpStatusCode.OK, MaxioPayloads.Customer())
            .Route(HttpMethod.Get, MaxioPayloads.CustomerSubscriptionsPath(), HttpStatusCode.OK, MaxioPayloads.SubscriptionList());

        var subscription = await harness.BuildService().SubscribeAsync(Demo, "eshop-pro");

        Assert.False(subscription.WasCreatedByThisRequest);
        Assert.Equal(94211243, subscription.Id);

        // The point of the whole exercise: a repeated subscribe writes nothing.
        Assert.Equal(0, harness.CountOf(HttpMethod.Post, MaxioPayloads.CreateSubscriptionPath));
        Assert.Equal(0, harness.CountOf(HttpMethod.Post, MaxioPayloads.CreateCustomerPath));
    }

    [Fact]
    public async Task SubscribeAsync_WhenPreviousSubscriptionWasCanceled_EnrollsAgain()
    {
        var harness = CatalogHarness()
            .Route(HttpMethod.Get, MaxioPayloads.CustomerLookupPath, HttpStatusCode.OK, MaxioPayloads.Customer())
            .Route(HttpMethod.Get, MaxioPayloads.CustomerSubscriptionsPath(), HttpStatusCode.OK,
                MaxioPayloads.SubscriptionList(state: "canceled"))
            .Route(HttpMethod.Post, MaxioPayloads.CreateSubscriptionPath, HttpStatusCode.Created,
                MaxioPayloads.Subscription(id: 94300000));

        var subscription = await harness.BuildService().SubscribeAsync(Demo, "eshop-pro");

        Assert.True(subscription.WasCreatedByThisRequest);
        Assert.Equal(94300000, subscription.Id);
        Assert.Equal(1, harness.CountOf(HttpMethod.Post, MaxioPayloads.CreateSubscriptionPath));
    }

    [Fact]
    public async Task SubscribeAsync_WhenSubscribedToADifferentPlan_StillEnrollsInTheRequestedOne()
    {
        var harness = CatalogHarness()
            .Route(HttpMethod.Get, MaxioPayloads.CustomerLookupPath, HttpStatusCode.OK, MaxioPayloads.Customer())
            .Route(HttpMethod.Get, MaxioPayloads.CustomerSubscriptionsPath(), HttpStatusCode.OK,
                MaxioPayloads.SubscriptionList(productHandle: "basic-plan"))
            .Route(HttpMethod.Post, MaxioPayloads.CreateSubscriptionPath, HttpStatusCode.Created, MaxioPayloads.Subscription());

        var subscription = await harness.BuildService().SubscribeAsync(Demo, "eshop-pro");

        Assert.True(subscription.WasCreatedByThisRequest);
        Assert.Contains(
            "\"product_handle\":\"eshop-pro\"",
            harness.Last(HttpMethod.Post, MaxioPayloads.CreateSubscriptionPath).Body);
    }

    [Fact]
    public async Task SubscribeAsync_WhenProviderRejectsTheSignup_SurfacesItAsACallerErrorWithDetail()
    {
        var harness = CatalogHarness()
            .Route(HttpMethod.Get, MaxioPayloads.CustomerLookupPath, HttpStatusCode.OK, MaxioPayloads.Customer())
            .Route(HttpMethod.Get, MaxioPayloads.CustomerSubscriptionsPath(), HttpStatusCode.OK, MaxioPayloads.EmptySubscriptionList)
            .Route(HttpMethod.Post, MaxioPayloads.CreateSubscriptionPath, HttpStatusCode.UnprocessableEntity,
                MaxioPayloads.NoPaymentMethodError);

        var exception = await Assert.ThrowsAsync<BillingException>(
            () => harness.BuildService().SubscribeAsync(Demo, "eshop-pro"));

        // A deterministic rejection must not look like an outage, or callers retry what can never succeed.
        Assert.Equal(BillingFailureKind.InvalidRequest, exception.Kind);
        Assert.Contains("No payment method was on file for the $299.00 balance", exception.Details);
    }

    [Fact]
    public async Task SubscribeAsync_WhenTransportFailsOnTheWrite_DoesNotResendTheEnrollment()
    {
        // The SDK retries transport failures on every verb, so without the send guard this POST would be
        // delivered up to four times - four subscriptions for one click.
        var harness = CatalogHarness()
            .Route(HttpMethod.Get, MaxioPayloads.CustomerLookupPath, HttpStatusCode.OK, MaxioPayloads.Customer())
            .Route(HttpMethod.Get, MaxioPayloads.CustomerSubscriptionsPath(), HttpStatusCode.OK, MaxioPayloads.EmptySubscriptionList)
            .RouteTransportFailure(HttpMethod.Post, MaxioPayloads.CreateSubscriptionPath);

        var exception = await Assert.ThrowsAsync<BillingException>(
            () => harness.BuildService().SubscribeAsync(Demo, "eshop-pro"));

        Assert.Equal(1, harness.CountOf(HttpMethod.Post, MaxioPayloads.CreateSubscriptionPath));

        // Reconciliation found nothing, and one send may still have been received - so the outcome is
        // unknown rather than a plain failure, and the caller must not be told to retry.
        Assert.Equal(BillingFailureKind.IndeterminateOutcome, exception.Kind);
    }

    [Fact]
    public async Task TransportFailuresAreRetriedOnReads_WhichIsWhyTheWriteNeedsAGuard()
    {
        // Control for the test above. If the SDK did not resend on transport failures, that test would
        // pass for the wrong reason - so pin the behaviour it is defending against: an unguarded request
        // is delivered more than once, on a verb no retry policy excludes.
        var harness = new MaxioTestHarness()
            .RouteTransportFailure(HttpMethod.Get, MaxioPayloads.CustomerLookupPath);

        await Assert.ThrowsAsync<BillingException>(() => harness.BuildService().GetSubscriptionsAsync(Demo));

        Assert.True(
            harness.CountOf(HttpMethod.Get, MaxioPayloads.CustomerLookupPath) > 1,
            "expected the SDK to resend after a transport failure");
    }

    [Fact]
    public async Task SubscribeAsync_WhenTheWriteLandedButTheAnswerWasLost_ReportsTheSubscriptionItFinds()
    {
        var harness = CatalogHarness()
            .Route(HttpMethod.Get, MaxioPayloads.CustomerLookupPath, HttpStatusCode.OK, MaxioPayloads.Customer())
            .RouteSequence(HttpMethod.Get, MaxioPayloads.CustomerSubscriptionsPath(),
                (HttpStatusCode.OK, MaxioPayloads.EmptySubscriptionList),   // pre-check: nothing yet
                (HttpStatusCode.OK, MaxioPayloads.SubscriptionList()))      // reconcile: the write did land
            .RouteTransportFailure(HttpMethod.Post, MaxioPayloads.CreateSubscriptionPath);

        var subscription = await harness.BuildService().SubscribeAsync(Demo, "eshop-pro");

        Assert.Equal(94211243, subscription.Id);
        Assert.False(subscription.WasCreatedByThisRequest);
        Assert.Equal(1, harness.CountOf(HttpMethod.Post, MaxioPayloads.CreateSubscriptionPath));
    }

    [Fact]
    public async Task SubscribeAsync_WhenAConcurrentRequestCreatedTheCustomerFirst_UsesTheWinner()
    {
        // The provider enforces one customer per reference, so a lost race answers 422 rather than
        // duplicating. Re-reading resolves it instead of failing the shopper's signup.
        var harness = CatalogHarness()
            .RouteSequence(HttpMethod.Get, MaxioPayloads.CustomerLookupPath,
                (HttpStatusCode.NotFound, MaxioPayloads.NotFound()),
                (HttpStatusCode.OK, MaxioPayloads.Customer()))
            .Route(HttpMethod.Post, MaxioPayloads.CreateCustomerPath, HttpStatusCode.UnprocessableEntity,
                """{ "errors": { "reference": ["has already been taken"] } }""")
            .Route(HttpMethod.Get, MaxioPayloads.CustomerSubscriptionsPath(), HttpStatusCode.OK, MaxioPayloads.EmptySubscriptionList)
            .Route(HttpMethod.Post, MaxioPayloads.CreateSubscriptionPath, HttpStatusCode.Created, MaxioPayloads.Subscription());

        var subscription = await harness.BuildService().SubscribeAsync(Demo, "eshop-pro");

        Assert.Equal(MaxioTestHarness.CustomerId, subscription.CustomerId);
        Assert.Equal(1, harness.CountOf(HttpMethod.Post, MaxioPayloads.CreateCustomerPath));
    }

    [Fact]
    public async Task SubscribeAsync_WhenTheLookupIsUnreadable_FailsRatherThanCreatingASecondCustomer()
    {
        // An unreadable answer is not "no such customer". Treating it as absence would duplicate the customer.
        var harness = CatalogHarness()
            .Route(HttpMethod.Get, MaxioPayloads.CustomerLookupPath, HttpStatusCode.OK, "{ \"customer\": ");

        var exception = await Assert.ThrowsAsync<BillingException>(
            () => harness.BuildService().SubscribeAsync(Demo, "eshop-pro"));

        Assert.Equal(BillingFailureKind.ProviderError, exception.Kind);
        Assert.Equal(0, harness.CountOf(HttpMethod.Post, MaxioPayloads.CreateCustomerPath));
        Assert.DoesNotContain("Json", exception.Message);
    }

    [Fact]
    public async Task SubscribeAsync_WithAnUnknownPlanHandle_RejectsAndNamesTheAvailablePlans()
    {
        var harness = CatalogHarness();

        var exception = await Assert.ThrowsAsync<BillingException>(
            () => harness.BuildService().SubscribeAsync(Demo, "no-such-plan"));

        Assert.Equal(BillingFailureKind.InvalidRequest, exception.Kind);
        Assert.Contains("eshop-pro", exception.Details);
        Assert.Contains("basic-plan", exception.Details);
        Assert.Equal(0, harness.CountOf(HttpMethod.Post, MaxioPayloads.CreateSubscriptionPath));
    }

    [Fact]
    public async Task SubscribeAsync_WithNoPlanAndNoDefault_RejectsWithoutTouchingTheProvider()
    {
        var harness = CatalogHarness();

        var exception = await Assert.ThrowsAsync<BillingException>(
            () => harness.BuildService().SubscribeAsync(Demo, null));

        Assert.Equal(BillingFailureKind.InvalidRequest, exception.Kind);
        Assert.Equal(0, harness.CountOf(HttpMethod.Post, MaxioPayloads.CreateSubscriptionPath));
    }

    [Fact]
    public async Task SubscribeAsync_WithAConfiguredDefaultPlan_UsesItWhenTheRequestNamesNone()
    {
        var harness = CatalogHarness()
            .Route(HttpMethod.Get, MaxioPayloads.CustomerLookupPath, HttpStatusCode.OK, MaxioPayloads.Customer())
            .Route(HttpMethod.Get, MaxioPayloads.CustomerSubscriptionsPath(), HttpStatusCode.OK, MaxioPayloads.EmptySubscriptionList)
            .Route(HttpMethod.Post, MaxioPayloads.CreateSubscriptionPath, HttpStatusCode.Created, MaxioPayloads.Subscription());
        harness.Settings.DefaultProductHandle = "eshop-pro";

        await harness.BuildService().SubscribeAsync(Demo, null);

        Assert.Contains(
            "\"product_handle\":\"eshop-pro\"",
            harness.Last(HttpMethod.Post, MaxioPayloads.CreateSubscriptionPath).Body);
    }

    [Fact]
    public async Task SubscribeAsync_WhenThePlanDemandsACard_RefusesBeforeWriting()
    {
        var harness = CatalogHarness(requireCreditCard: true);

        var exception = await Assert.ThrowsAsync<BillingException>(
            () => harness.BuildService().SubscribeAsync(Demo, "eshop-pro"));

        Assert.Equal(BillingFailureKind.InvalidRequest, exception.Kind);
        Assert.Equal(0, harness.CountOf(HttpMethod.Post, MaxioPayloads.CreateSubscriptionPath));
    }

    [Fact]
    public async Task GetSubscriptionsAsync_WhenTheUserHasNeverSubscribed_ReturnsEmpty()
    {
        var harness = new MaxioTestHarness()
            .Route(HttpMethod.Get, MaxioPayloads.CustomerLookupPath, HttpStatusCode.NotFound, MaxioPayloads.NotFound());

        var subscriptions = await harness.BuildService().GetSubscriptionsAsync(Demo);

        Assert.Empty(subscriptions);
        Assert.Equal(0, harness.CountOf(HttpMethod.Get, MaxioPayloads.CustomerSubscriptionsPath()));
    }

    [Fact]
    public async Task GetSubscriptionsAsync_LooksTheCustomerUpByReferenceAndProjectsWhatItFinds()
    {
        var harness = new MaxioTestHarness()
            .Route(HttpMethod.Get, MaxioPayloads.CustomerLookupPath, HttpStatusCode.OK, MaxioPayloads.Customer())
            .Route(HttpMethod.Get, MaxioPayloads.CustomerSubscriptionsPath(), HttpStatusCode.OK, MaxioPayloads.SubscriptionList());

        var subscriptions = await harness.BuildService().GetSubscriptionsAsync(Demo);

        var subscription = Assert.Single(subscriptions);
        Assert.Equal(94211243, subscription.Id);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal(29900, subscription.PriceInCents);
        Assert.Equal("USD", subscription.Currency);
        Assert.Equal("remittance", subscription.PaymentCollectionMethod);
        Assert.NotNull(subscription.NextBillingAt);

        // No stored mapping is involved: the reference is derived from the user name on every call, which
        // is what lets subscriptions survive a restart of the in-memory database.
        Assert.Contains(
            WebUtility.UrlEncode(MaxioTestHarness.CustomerReference),
            harness.Last(HttpMethod.Get, MaxioPayloads.CustomerLookupPath).Query);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_WhenTheProviderRejectsOurCredentials_SaysSoWithoutLeakingDetail()
    {
        var harness = new MaxioTestHarness()
            .Route(HttpMethod.Get, MaxioPayloads.CustomerLookupPath, HttpStatusCode.Unauthorized,
                """{"error":"bad credentials"}""");

        var exception = await Assert.ThrowsAsync<BillingException>(
            () => harness.BuildService().GetSubscriptionsAsync(Demo));

        Assert.Equal(BillingFailureKind.Unauthorized, exception.Kind);
        Assert.DoesNotContain("bad credentials", exception.Message);
    }
}
