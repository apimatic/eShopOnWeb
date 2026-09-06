using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private static readonly BillingCustomerIdentity Shopper =
        BillingCustomerIdentity.FromEmail("demouser@microsoft.com");

    private static StubMaxioTransport CatalogTransport() =>
        new StubMaxioTransport()
            .Respond(HttpMethod.Get, "/site.json", HttpStatusCode.OK, MaxioFixtures.Site)
            .Respond(HttpMethod.Get, "/product_families.json", HttpStatusCode.OK, MaxioFixtures.ProductFamilies)
            .Respond(HttpMethod.Get, "/products.json", HttpStatusCode.OK, MaxioFixtures.Products);

    /// <summary>A shopper who has a billing customer but is not yet subscribed to anything.</summary>
    private static StubMaxioTransport SubscribeTransport(string createStatusJson = MaxioFixtures.CreatedProSubscription) =>
        new StubMaxioTransport()
            .Respond(HttpMethod.Get, "/site.json", HttpStatusCode.OK, MaxioFixtures.Site)
            .Respond(HttpMethod.Get, "/products/handle/", HttpStatusCode.OK, MaxioFixtures.ProProduct)
            .Respond(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioFixtures.Customer)
            .Respond(HttpMethod.Get, "/subscriptions.json", HttpStatusCode.OK, MaxioFixtures.NoSubscriptions)
            .Respond(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created, createStatusJson);

    // -----------------------------------------------------------------------------------------------------
    // Plans
    // -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task GetPlansAsync_ProjectsThePublishedPlans()
    {
        var service = MaxioTestHost.CreateService(CatalogTransport());

        var plans = await service.GetPlansAsync();

        Assert.Equal(new[] { "basic-plan", "eshop-pro" }, plans.Select(plan => plan.Handle));

        var pro = plans.Single(plan => plan.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal(299m, pro.Price);
        Assert.Equal("USD", pro.Currency);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.False(pro.RequiresPaymentMethod);
    }

    [Fact]
    public async Task GetPlansAsync_ExcludesArchivedProducts()
    {
        var service = MaxioTestHost.CreateService(CatalogTransport());

        var plans = await service.GetPlansAsync();

        Assert.DoesNotContain(plans, plan => plan.Handle == "retired-plan");
    }

    [Fact]
    public async Task GetPlansAsync_ResolvesTheProductFamilyByHandleRatherThanByAHardCodedId()
    {
        var transport = CatalogTransport();
        var service = MaxioTestHost.CreateService(transport);

        await service.GetPlansAsync();

        // The id in the products path can only have come from matching the configured handle against the
        // family listing - it is nowhere in configuration or in the code.
        Assert.Contains(transport.Requests, request => request.Path.Contains("/product_families/3026729/products.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetPlansAsync_CachesTheResolvedProductFamilyAcrossCalls()
    {
        var transport = CatalogTransport();
        var service = MaxioTestHost.CreateService(transport);

        await service.GetPlansAsync();
        await service.GetPlansAsync();

        Assert.Equal(1, transport.CountOf(HttpMethod.Get, "/product_families.json"));
        Assert.Equal(2, transport.CountOf(HttpMethod.Get, "/product_families/3026729/products.json"));
    }

    [Fact]
    public async Task GetPlansAsync_WhenTheConfiguredFamilyHandleIsUnknown_ReportsMisconfiguration()
    {
        var settings = MaxioTestHost.DefaultSettings();
        settings.ProductFamilyHandle = "not-on-this-site";

        var service = MaxioTestHost.CreateService(CatalogTransport(), settings);

        var exception = await Assert.ThrowsAsync<BillingException>(() => service.GetPlansAsync());

        Assert.Equal(BillingFailureKind.Misconfigured, exception.Kind);
        Assert.Contains("not-on-this-site", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPlansAsync_WhenTheSiteCannotBeRead_StillReturnsPlansWithoutACurrency()
    {
        var transport = new StubMaxioTransport()
            .Respond(HttpMethod.Get, "/site.json", HttpStatusCode.InternalServerError, MaxioFixtures.NotFound)
            .Respond(HttpMethod.Get, "/product_families.json", HttpStatusCode.OK, MaxioFixtures.ProductFamilies)
            .Respond(HttpMethod.Get, "/products.json", HttpStatusCode.OK, MaxioFixtures.Products);

        var plans = await MaxioTestHost.CreateService(transport).GetPlansAsync();

        Assert.NotEmpty(plans);
        Assert.All(plans, plan => Assert.Null(plan.Currency));
    }

    // -----------------------------------------------------------------------------------------------------
    // Subscribe - the hero flow
    // -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task SubscribeAsync_EnrollsTheShopperAndConfirmsPlanPriceStateAndNextBillingDate()
    {
        var service = MaxioTestHost.CreateService(SubscribeTransport());

        var result = await service.SubscribeAsync(Shopper, "eshop-pro");

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(94209629, result.Subscription.Id);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal("Pro Plan", result.Subscription.PlanName);
        Assert.Equal("active", result.Subscription.State);
        Assert.True(result.Subscription.IsActive);
        Assert.Equal(29900, result.Subscription.PriceInCents);
        Assert.Equal(299m, result.Subscription.Price);
        Assert.Equal("USD", result.Subscription.Currency);
        Assert.NotNull(result.Subscription.NextBillingAt);
    }

    [Fact]
    public async Task SubscribeAsync_SendsTheShopperReferenceAndPlanHandleButNeverAReferralCode()
    {
        var transport = SubscribeTransport();

        await MaxioTestHost.CreateService(transport).SubscribeAsync(Shopper, "eshop-pro");

        var create = transport.Requests.Single(request =>
            request.Method == HttpMethod.Post && request.Path.EndsWith("/subscriptions.json", StringComparison.Ordinal));

        Assert.Contains("\"product_handle\":\"eshop-pro\"", create.Body, StringComparison.Ordinal);
        Assert.Contains($"\"reference\":\"{Shopper.SubscriptionReference("eshop-pro")}\"", create.Body, StringComparison.Ordinal);

        // 'ref' is a referral code, not the application reference: an invalid value fails creation outright.
        Assert.DoesNotContain("\"ref\":", create.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubscribeAsync_AsksToBeBilledRatherThanChargedSoNoCardIsNeeded()
    {
        var transport = SubscribeTransport();

        await MaxioTestHost.CreateService(transport).SubscribeAsync(Shopper, "eshop-pro");

        var create = transport.Requests.Single(request =>
            request.Method == HttpMethod.Post && request.Path.EndsWith("/subscriptions.json", StringComparison.Ordinal));

        // The fixture site runs Relationship Invoicing, so remittance is the valid non-automatic method.
        Assert.Contains("\"payment_collection_method\":\"remittance\"", create.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("credit_card", create.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("payment_profile", create.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubscribeAsync_OnALegacyStatementsSite_UsesInvoiceCollectionInstead()
    {
        var legacySite = MaxioFixtures.Site.Replace(
            "\"relationship_invoicing_enabled\": true",
            "\"relationship_invoicing_enabled\": false",
            StringComparison.Ordinal);

        var transport = new StubMaxioTransport()
            .Respond(HttpMethod.Get, "/site.json", HttpStatusCode.OK, legacySite)
            .Respond(HttpMethod.Get, "/products/handle/", HttpStatusCode.OK, MaxioFixtures.ProProduct)
            .Respond(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioFixtures.Customer)
            .Respond(HttpMethod.Get, "/subscriptions.json", HttpStatusCode.OK, MaxioFixtures.NoSubscriptions)
            .Respond(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created, MaxioFixtures.CreatedProSubscription);

        await MaxioTestHost.CreateService(transport).SubscribeAsync(Shopper, "eshop-pro");

        var create = transport.Requests.Single(request =>
            request.Method == HttpMethod.Post && request.Path.EndsWith("/subscriptions.json", StringComparison.Ordinal));

        Assert.Contains("\"payment_collection_method\":\"invoice\"", create.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubscribeAsync_WhenTheShopperHasNoBillingCustomer_CreatesExactlyOne()
    {
        var transport = new StubMaxioTransport()
            .Respond(HttpMethod.Get, "/site.json", HttpStatusCode.OK, MaxioFixtures.Site)
            .Respond(HttpMethod.Get, "/products/handle/", HttpStatusCode.OK, MaxioFixtures.ProProduct)
            .Respond(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.NotFound, MaxioFixtures.NotFound)
            .Respond(HttpMethod.Post, "/customers.json", HttpStatusCode.Created, MaxioFixtures.Customer)
            .Respond(HttpMethod.Get, "/subscriptions.json", HttpStatusCode.OK, MaxioFixtures.NoSubscriptions)
            .Respond(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created, MaxioFixtures.CreatedProSubscription);

        var result = await MaxioTestHost.CreateService(transport).SubscribeAsync(Shopper, "eshop-pro");

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(1, transport.CountOf(HttpMethod.Post, "/customers.json"));
        Assert.Equal(1, transport.CountOf(HttpMethod.Post, "/subscriptions.json"));

        var create = transport.Requests.Single(request =>
            request.Method == HttpMethod.Post && request.Path.EndsWith("/customers.json", StringComparison.Ordinal));

        Assert.Contains($"\"reference\":\"{Shopper.CustomerReference}\"", create.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubscribeAsync_WhenAlreadySubscribed_ReturnsTheExistingSubscriptionAndWritesNothing()
    {
        var transport = new StubMaxioTransport()
            .Respond(HttpMethod.Get, "/site.json", HttpStatusCode.OK, MaxioFixtures.Site)
            .Respond(HttpMethod.Get, "/products/handle/", HttpStatusCode.OK, MaxioFixtures.ProProduct)
            .Respond(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioFixtures.Customer)
            .Respond(HttpMethod.Get, "/subscriptions.json", HttpStatusCode.OK, MaxioFixtures.ActiveProSubscription);

        var result = await MaxioTestHost.CreateService(transport).SubscribeAsync(Shopper, "eshop-pro");

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(94209629, result.Subscription.Id);
        Assert.Equal(0, transport.CountOf(HttpMethod.Post, "/subscriptions.json"));
        Assert.Equal(0, transport.CountOf(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public async Task SubscribeAsync_WhenTheOnlySubscriptionWasCancelled_EnrollsTheShopperAgain()
    {
        var transport = new StubMaxioTransport()
            .Respond(HttpMethod.Get, "/site.json", HttpStatusCode.OK, MaxioFixtures.Site)
            .Respond(HttpMethod.Get, "/products/handle/", HttpStatusCode.OK, MaxioFixtures.ProProduct)
            .Respond(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioFixtures.Customer)
            .Respond(HttpMethod.Get, "/subscriptions.json", HttpStatusCode.OK, MaxioFixtures.CanceledProSubscription)
            .Respond(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created, MaxioFixtures.CreatedProSubscription);

        var result = await MaxioTestHost.CreateService(transport).SubscribeAsync(Shopper, "eshop-pro");

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(1, transport.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_UnderConcurrentDoubleClicks_CreatesAtMostOneSubscription()
    {
        var writes = 0;

        var transport = new StubMaxioTransport()
            .Respond(HttpMethod.Get, "/site.json", HttpStatusCode.OK, MaxioFixtures.Site)
            .Respond(HttpMethod.Get, "/products/handle/", HttpStatusCode.OK, MaxioFixtures.ProProduct)
            .Respond(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioFixtures.Customer)

            // Once the write lands, Maxio reports the subscription on the next read, as it does live.
            .Respond(HttpMethod.Get, "/subscriptions.json", () => Volatile.Read(ref writes) == 0
                ? (HttpStatusCode.OK, MaxioFixtures.NoSubscriptions)
                : (HttpStatusCode.OK, MaxioFixtures.ActiveProSubscription))
            .Respond(HttpMethod.Post, "/subscriptions.json", () =>
            {
                Interlocked.Increment(ref writes);
                return (HttpStatusCode.Created, MaxioFixtures.CreatedProSubscription);
            });

        var service = MaxioTestHost.CreateService(transport);

        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => service.SubscribeAsync(Shopper, "eshop-pro")));

        Assert.Equal(1, transport.CountOf(HttpMethod.Post, "/subscriptions.json"));
        Assert.Single(results, result => !result.AlreadySubscribed);
        Assert.All(results, result => Assert.Equal(94209629, result.Subscription.Id));
    }

    [Fact]
    public async Task SubscribeAsync_WhenThePlanRequiresACard_IsRejectedBeforeAnythingIsWritten()
    {
        var transport = new StubMaxioTransport()
            .Respond(HttpMethod.Get, "/products/handle/", HttpStatusCode.OK, MaxioFixtures.CardRequiredProduct)
            .Respond(HttpMethod.Get, "/site.json", HttpStatusCode.OK, MaxioFixtures.Site)
            .Respond(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioFixtures.Customer)
            .Respond(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created, MaxioFixtures.CreatedProSubscription);

        var service = MaxioTestHost.CreateService(transport);

        var exception = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(Shopper, "eshop-pro"));

        Assert.Equal(BillingFailureKind.Validation, exception.Kind);
        Assert.Equal(0, transport.CountOf(HttpMethod.Post, "/subscriptions.json"));
        Assert.Equal(0, transport.CountOf(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public async Task SubscribeAsync_WhenTheUnknownPlanIsRequested_ReportsPlanNotFound()
    {
        var transport = new StubMaxioTransport()
            .Respond(HttpMethod.Get, "/products/handle/", HttpStatusCode.NotFound, MaxioFixtures.NotFound);

        var service = MaxioTestHost.CreateService(transport);

        var exception = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(Shopper, "no-such-plan"));

        Assert.Equal(BillingFailureKind.PlanNotFound, exception.Kind);
        Assert.Equal((int)HttpStatusCode.NotFound, exception.ProviderStatusCode);
    }

    [Fact]
    public async Task SubscribeAsync_WhenMaxioRejectsTheSubscription_SurfacesTheReasonAsAValidationFailure()
    {
        var transport = new StubMaxioTransport()
            .Respond(HttpMethod.Get, "/site.json", HttpStatusCode.OK, MaxioFixtures.Site)
            .Respond(HttpMethod.Get, "/products/handle/", HttpStatusCode.OK, MaxioFixtures.ProProduct)
            .Respond(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioFixtures.Customer)
            .Respond(HttpMethod.Get, "/subscriptions.json", HttpStatusCode.OK, MaxioFixtures.NoSubscriptions)
            .Respond(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.UnprocessableEntity, MaxioFixtures.NoPaymentMethodOnFile);

        var service = MaxioTestHost.CreateService(transport);

        var exception = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(Shopper, "eshop-pro"));

        Assert.Equal(BillingFailureKind.Validation, exception.Kind);
        Assert.Equal((int)HttpStatusCode.UnprocessableEntity, exception.ProviderStatusCode);
        Assert.Contains("No payment method was on file", exception.Message, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------------------------------------
    // Write-once under a transport fault
    // -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task SubscribeAsync_WhenTheConnectionDropsOnTheWrite_TheWriteIsNeverResent()
    {
        // A dropped connection is retried by the SDK on every verb, POST included, and retries cannot be
        // switched off - so without the write-once guard this is where a duplicate enrollment comes from.
        var transport = new StubMaxioTransport()
            .Respond(HttpMethod.Get, "/site.json", HttpStatusCode.OK, MaxioFixtures.Site)
            .Respond(HttpMethod.Get, "/products/handle/", HttpStatusCode.OK, MaxioFixtures.ProProduct)
            .Respond(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioFixtures.Customer)
            .Respond(HttpMethod.Get, "/subscriptions.json", HttpStatusCode.OK, MaxioFixtures.NoSubscriptions)
            .Fail(HttpMethod.Post, "/subscriptions.json", new HttpRequestException("connection reset"))
            .RespondToAnythingElse(HttpStatusCode.NotFound, MaxioFixtures.NotFound);

        var service = MaxioTestHost.CreateService(transport);

        var exception = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(Shopper, "eshop-pro"));

        Assert.Equal(1, transport.CountOf(HttpMethod.Post, "/subscriptions.json"));

        // The one send we allowed may or may not have landed, and reconciliation found nothing. That is an
        // unknown outcome, not a failure the caller may safely retry.
        Assert.Equal(BillingFailureKind.UnknownOutcome, exception.Kind);
    }

    [Fact]
    public async Task SubscribeAsync_WhenTheConnectionDropsButTheWriteLanded_ReconcilesInsteadOfDuplicating()
    {
        var transport = new StubMaxioTransport()
            .Respond(HttpMethod.Get, "/site.json", HttpStatusCode.OK, MaxioFixtures.Site)
            .Respond(HttpMethod.Get, "/products/handle/", HttpStatusCode.OK, MaxioFixtures.ProProduct)
            .Respond(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioFixtures.Customer)
            .Respond(HttpMethod.Get, "/subscriptions.json", HttpStatusCode.OK, MaxioFixtures.NoSubscriptions)
            .Fail(HttpMethod.Post, "/subscriptions.json", new HttpRequestException("connection reset"))

            // The reconciliation read: the subscription is there, so the response was lost, not the write.
            .RespondToAnythingElse(HttpStatusCode.OK, MaxioFixtures.CreatedProSubscription);

        var result = await MaxioTestHost.CreateService(transport).SubscribeAsync(Shopper, "eshop-pro");

        Assert.Equal(94209629, result.Subscription.Id);
        Assert.Equal(1, transport.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_WhenTheConnectionDropsCreatingTheCustomer_TheCustomerWriteIsNeverResent()
    {
        var transport = new StubMaxioTransport()
            .Respond(HttpMethod.Get, "/site.json", HttpStatusCode.OK, MaxioFixtures.Site)
            .Respond(HttpMethod.Get, "/products/handle/", HttpStatusCode.OK, MaxioFixtures.ProProduct)
            .Respond(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.NotFound, MaxioFixtures.NotFound)
            .Fail(HttpMethod.Post, "/customers.json", new HttpRequestException("connection reset"));

        var service = MaxioTestHost.CreateService(transport);

        var exception = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(Shopper, "eshop-pro"));

        Assert.Equal(1, transport.CountOf(HttpMethod.Post, "/customers.json"));
        Assert.Equal(BillingFailureKind.UnknownOutcome, exception.Kind);
    }

    // -----------------------------------------------------------------------------------------------------
    // Listing and configuration
    // -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task GetSubscriptionsAsync_WhenTheShopperHasNoBillingCustomer_ReturnsNothing()
    {
        var transport = new StubMaxioTransport()
            .Respond(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.NotFound, MaxioFixtures.NotFound);

        var subscriptions = await MaxioTestHost.CreateService(transport).GetSubscriptionsAsync(Shopper);

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_ExcludesCancelledSubscriptionsUnlessAskedFor()
    {
        var transport = new StubMaxioTransport()
            .Respond(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioFixtures.Customer)
            .Respond(HttpMethod.Get, "/subscriptions.json", HttpStatusCode.OK, MaxioFixtures.CanceledProSubscription);

        var service = MaxioTestHost.CreateService(transport);

        Assert.Empty(await service.GetSubscriptionsAsync(Shopper));

        var withInactive = await service.GetSubscriptionsAsync(Shopper, includeInactive: true);

        var cancelled = Assert.Single(withInactive);
        Assert.Equal("canceled", cancelled.State);
        Assert.False(cancelled.IsActive);
        Assert.NotNull(cancelled.CanceledAt);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_WhenMaxioRejectsOurCredentials_IsNeverBlamedOnTheCaller()
    {
        var transport = new StubMaxioTransport()
            .Respond(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.Unauthorized, MaxioFixtures.NotFound);

        var service = MaxioTestHost.CreateService(transport);

        var exception = await Assert.ThrowsAsync<BillingException>(() => service.GetSubscriptionsAsync(Shopper));

        Assert.Equal(BillingFailureKind.ProviderUnauthorized, exception.Kind);
        Assert.DoesNotContain("test-key", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnyOperation_WhenBillingIsNotConfigured_ReportsItRatherThanCallingMaxio()
    {
        var accessor = new MaxioClientAccessor(new[] { "'Maxio:ApiKey' is not set." });
        var service = MaxioTestHost.CreateService(accessor);

        var exception = await Assert.ThrowsAsync<BillingException>(() => service.GetPlansAsync());

        Assert.Equal(BillingFailureKind.NotConfigured, exception.Kind);
        Assert.Contains("Maxio:ApiKey", exception.Message, StringComparison.Ordinal);
        Assert.False(accessor.IsConfigured);
    }
}
