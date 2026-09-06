using System.Net;
using Microsoft.eShopWeb.MaxioBilling.Exceptions;
using Microsoft.eShopWeb.MaxioBilling.Models;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.MaxioBilling;

public class MaxioSubscriptionBillingServiceTests
{
    private static readonly SubscriberIdentity Subscriber = SubscriberIdentity.ForUser("someone@example.com");

    [Fact]
    public async Task GetPlansAsyncMapsProductsOfTheConfiguredFamilyWithTheSiteCurrency()
    {
        var (service, _) = MaxioTestHost.Build(new MaxioApiFake());

        var plans = await service.GetPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal(MaxioTestHost.PlanHandle, plan.Handle);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal("month", plan.IntervalUnit);
        // Currency is not carried on a Maxio product, so it has to come from the site read.
        Assert.Equal("USD", plan.Currency);
        // require_credit_card is false, request_credit_card is true: reported separately, because
        // only the former blocks a create.
        Assert.False(plan.PaymentMethodRequired);
        Assert.True(plan.PaymentMethodRequested);
    }

    [Fact]
    public async Task SubscribeAsyncCreatesTheCustomerWhenTheLookupReturnsNotFound()
    {
        var fake = new MaxioApiFake { CustomerExists = false };
        var (service, handler) = MaxioTestHost.Build(fake);

        var result = await service.SubscribeAsync(Subscriber, MaxioTestHost.PlanHandle);

        Assert.False(result.AlreadyExisted);
        Assert.Equal(MaxioApiFake.CreatedSubscriptionId, result.Subscription.Id);
        // A 404 from the reference lookup means "absent", not "failed".
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/customers"));
    }

    [Fact]
    public async Task SubscribeAsyncSendsTheHandleTheCustomerIdAndAnInvoiceCollectionMethod()
    {
        var fake = new MaxioApiFake { CustomerExists = true };
        var (service, handler) = MaxioTestHost.Build(fake);

        await service.SubscribeAsync(Subscriber, MaxioTestHost.PlanHandle);

        var body = handler.LastBodyFor(HttpMethod.Post, "/subscriptions");
        Assert.NotNull(body);
        Assert.Contains("\"product_handle\":", body);
        Assert.Contains(MaxioTestHost.PlanHandle, body);
        Assert.Contains($"\"customer_id\":{MaxioApiFake.ExistingCustomerId}", body);
        // The site is relationship-invoicing, so the invoice-style member for that architecture is
        // "remittance". Without it Maxio tries to auto-charge and rejects for lack of a card.
        Assert.Contains("\"payment_collection_method\":\"remittance\"", body);
        // Nothing that would capture or demand a payment method.
        Assert.DoesNotContain("credit_card", body);
        Assert.DoesNotContain("payment_profile", body);
    }

    [Fact]
    public async Task SubscribeAsyncUsesTheLegacyInvoiceMethodOnAStatementsSite()
    {
        var fake = new MaxioApiFake { CustomerExists = true, RelationshipInvoicingEnabled = false };
        var (service, handler) = MaxioTestHost.Build(fake);

        await service.SubscribeAsync(Subscriber, MaxioTestHost.PlanHandle);

        // "remittance" is rejected on a legacy Statements site and "invoice" on a relationship-
        // invoicing one, so the member is derived from the site rather than hardcoded.
        Assert.Contains("\"payment_collection_method\":\"invoice\"",
            handler.LastBodyFor(HttpMethod.Post, "/subscriptions"));
    }

    [Fact]
    public async Task SubscribeAsyncHonoursAnExplicitlyConfiguredCollectionMethod()
    {
        var fake = new MaxioApiFake { CustomerExists = true };
        var (service, handler) = MaxioTestHost.Build(
            fake, new Dictionary<string, string?> { ["Maxio:PaymentCollectionMethod"] = "prepaid" });

        await service.SubscribeAsync(Subscriber, MaxioTestHost.PlanHandle);

        Assert.Contains("\"payment_collection_method\":\"prepaid\"",
            handler.LastBodyFor(HttpMethod.Post, "/subscriptions"));
    }

    [Fact]
    public async Task SubscribeAsyncSendsNoCollectionMethodWhenConfiguredToUseTheSiteDefault()
    {
        var fake = new MaxioApiFake { CustomerExists = true };
        var (service, handler) = MaxioTestHost.Build(
            fake, new Dictionary<string, string?> { ["Maxio:PaymentCollectionMethod"] = "site-default" });

        await service.SubscribeAsync(Subscriber, MaxioTestHost.PlanHandle);

        Assert.DoesNotContain("payment_collection_method",
            handler.LastBodyFor(HttpMethod.Post, "/subscriptions")!);
    }

    [Fact]
    public async Task SubscribeAsyncReturnsTheExistingSubscriptionWithoutCreatingASecond()
    {
        var fake = new MaxioApiFake { CustomerExists = true };
        fake.ExistingSubscriptions.Add((4242, MaxioTestHost.PlanHandle, "active"));
        var (service, handler) = MaxioTestHost.Build(fake);

        var result = await service.SubscribeAsync(Subscriber, MaxioTestHost.PlanHandle);

        Assert.True(result.AlreadyExisted);
        Assert.Equal(4242, result.Subscription.Id);
        // The point of the guard: no write reached Maxio at all.
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/subscriptions"));
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/customers"));
    }

    [Fact]
    public async Task SubscribeAsyncStillCreatesWhenTheOnlyExistingSubscriptionIsCanceled()
    {
        var fake = new MaxioApiFake { CustomerExists = true };
        fake.ExistingSubscriptions.Add((4242, MaxioTestHost.PlanHandle, "canceled"));
        var (service, handler) = MaxioTestHost.Build(fake);

        var result = await service.SubscribeAsync(Subscriber, MaxioTestHost.PlanHandle);

        // A cancelled subscription must not block re-subscribing.
        Assert.False(result.AlreadyExisted);
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/subscriptions"));
    }

    [Fact]
    public async Task SubscribeAsyncIgnoresALiveSubscriptionOnADifferentPlan()
    {
        var fake = new MaxioApiFake { CustomerExists = true };
        fake.ExistingSubscriptions.Add((4242, "some-other-plan", "active"));
        var (service, handler) = MaxioTestHost.Build(fake);

        var result = await service.SubscribeAsync(Subscriber, MaxioTestHost.PlanHandle);

        Assert.False(result.AlreadyExisted);
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/subscriptions"));
    }

    [Fact]
    public async Task ConcurrentSubscribeRequestsCreateExactlyOneSubscription()
    {
        var fake = new MaxioApiFake { CustomerExists = false };
        var (service, handler) = MaxioTestHost.Build(fake);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => service.SubscribeAsync(Subscriber, MaxioTestHost.PlanHandle)));

        // Maxio documents no uniqueness for a subscription and offers no idempotency key, so the
        // per-subscriber lock across check-then-create is what holds this at one.
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/subscriptions"));
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/customers"));
        Assert.Single(results.Select(result => result.Subscription.Id).Distinct());
        Assert.Equal(1, results.Count(result => !result.AlreadyExisted));
    }

    [Fact]
    public async Task ATransportFaultOnCreateIsNotResentAndTheOutcomeIsReconciled()
    {
        var fake = new MaxioApiFake
        {
            CustomerExists = true,
            // A reset thrown after the bytes reached Maxio is indistinguishable from one thrown
            // before, and the SDK retries HttpRequestException on every verb — including POST.
            CreateSubscriptionTransportFault = new HttpRequestException("connection reset")
        };
        var (service, handler) = MaxioTestHost.Build(fake);

        await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(Subscriber, MaxioTestHost.PlanHandle));

        // The write-once guard must hold the send count at one even though the SDK asked for a retry.
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/subscriptions"));
    }

    [Fact]
    public async Task AnUnknownOutcomeIsSettledByReReadingMaxioRatherThanFailing()
    {
        var fake = new MaxioApiFake { CustomerExists = true };
        var (service, _) = MaxioTestHost.Build(fake);

        // Maxio applied the create, then the connection dropped before the answer came back.
        fake.ExistingSubscriptions.Add((4242, MaxioTestHost.PlanHandle, "active"));
        fake.CreateSubscriptionTransportFault = new HttpRequestException("connection reset");

        var result = await service.SubscribeAsync(Subscriber, MaxioTestHost.PlanHandle);

        // Reconciled, not reported as a failure the caller would retry into a duplicate.
        Assert.True(result.AlreadyExisted);
        Assert.Equal(4242, result.Subscription.Id);
    }

    [Fact]
    public async Task AValidationRejectionSurfacesAsARejectedFailureCarryingMaxiosReason()
    {
        var fake = new MaxioApiFake
        {
            CustomerExists = true,
            CreateSubscriptionFailure = (HttpStatusCode.UnprocessableEntity,
                """{"errors":["No payment method was on file for the $299.00 balance"]}""")
        };
        var (service, _) = MaxioTestHost.Build(fake);

        var exception = await Assert.ThrowsAsync<BillingException>(
            () => service.SubscribeAsync(Subscriber, MaxioTestHost.PlanHandle));

        Assert.Equal(BillingFailureKind.Rejected, exception.Kind);
        Assert.Equal(422, exception.ProviderStatusCode);
        Assert.Contains("No payment method was on file", exception.Message);
    }

    [Fact]
    public async Task AnUnknownPlanHandleIsAPlanNotFoundFailureAndNeverReachesTheWriteEndpoint()
    {
        var (service, handler) = MaxioTestHost.Build(new MaxioApiFake { CustomerExists = true });

        var exception = await Assert.ThrowsAsync<BillingException>(
            () => service.SubscribeAsync(Subscriber, "not-a-real-plan"));

        Assert.Equal(BillingFailureKind.PlanNotFound, exception.Kind);
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/subscriptions"));
    }

    [Fact]
    public async Task AMisconfiguredProductFamilyFailsLoudlyInsteadOfListingTheWholeSite()
    {
        var (service, _) = MaxioTestHost.Build(
            new MaxioApiFake(),
            new Dictionary<string, string?> { ["Maxio:ProductFamilyHandle"] = "no-such-family" });

        var exception = await Assert.ThrowsAsync<BillingException>(() => service.GetPlansAsync());

        Assert.Equal(BillingFailureKind.Configuration, exception.Kind);
    }

    [Fact]
    public async Task MissingCredentialsAreReportedAsNotConfiguredRatherThanCrashingAtStartup()
    {
        var (service, handler) = MaxioTestHost.Build(
            new MaxioApiFake(),
            new Dictionary<string, string?> { ["Maxio:ApiKey"] = null });

        Assert.False(service.IsConfigured);

        var exception = await Assert.ThrowsAsync<BillingException>(() => service.GetPlansAsync());

        Assert.Equal(BillingFailureKind.NotConfigured, exception.Kind);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetSubscriptionsAsyncReturnsEmptyForAUserWhoNeverSubscribed()
    {
        var (service, _) = MaxioTestHost.Build(new MaxioApiFake { CustomerExists = false });

        // An unknown customer is an empty list, not an error.
        Assert.Empty(await service.GetSubscriptionsAsync(Subscriber));
    }

    [Fact]
    public async Task GetSubscriptionsAsyncMapsPlanPriceStateAndNextBillingDate()
    {
        var fake = new MaxioApiFake { CustomerExists = true };
        fake.ExistingSubscriptions.Add((4242, MaxioTestHost.PlanHandle, "active"));
        var (service, _) = MaxioTestHost.Build(fake);

        var subscription = Assert.Single(await service.GetSubscriptionsAsync(Subscriber));

        Assert.Equal(4242, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal(MaxioTestHost.PlanHandle, subscription.PlanHandle);
        Assert.Equal(29900, subscription.PriceInCents);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero), subscription.NextAssessmentAt);
    }

    [Fact]
    public async Task SubscribeAsyncFallsBackToTheConfiguredDefaultPlanWhenNoneIsRequested()
    {
        var (service, handler) = MaxioTestHost.Build(new MaxioApiFake { CustomerExists = true });

        await service.SubscribeAsync(Subscriber, planHandle: null);

        Assert.Contains(MaxioTestHost.PlanHandle, handler.LastBodyFor(HttpMethod.Post, "/subscriptions"));
    }

    [Fact]
    public async Task SubscribeAsyncRejectsARequestWithNoPlanAndNoConfiguredDefault()
    {
        var (service, handler) = MaxioTestHost.Build(
            new MaxioApiFake { CustomerExists = true },
            new Dictionary<string, string?> { ["Maxio:DefaultPlanHandle"] = null });

        var exception = await Assert.ThrowsAsync<BillingException>(
            () => service.SubscribeAsync(Subscriber, planHandle: null));

        Assert.Equal(BillingFailureKind.Rejected, exception.Kind);
        Assert.Empty(handler.Requests);
    }
}
