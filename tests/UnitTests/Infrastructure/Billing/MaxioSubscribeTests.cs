using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;
using static Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.MaxioTestHarness;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscribeTests
{
    private static readonly SubscriberIdentity Subscriber = new(SubscriberEmail);

    private static MaxioStubHandler NewShopper() =>
        new MaxioStubHandler().WithFamily().WithSeededPlans().WithNoCustomer().WithCustomerCreated().WithNoSubscriptions();

    [Fact]
    public async Task CreatesTheCustomerAndTheSubscriptionForANewShopper()
    {
        var stub = NewShopper().On(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created, SubscriptionJson());

        var result = await ServiceOver(stub).SubscribeAsync(Subscriber, "eshop-pro");

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(900, result.Subscription.Id);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(29900L, result.Subscription.PriceInCents);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero), result.Subscription.NextBillingDate);
        Assert.Equal(1, stub.CountOf(HttpMethod.Post, "/customers.json"));
        Assert.Equal(1, stub.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SendsTheProductHandleTheCustomerIdAndAFutureFirstBillingDate()
    {
        // Without a future next_billing_at the provider assesses the first period at signup and refuses
        // the whole create when there is no payment profile — which is every signup on these plans.
        var stub = NewShopper().On(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created, SubscriptionJson());

        await ServiceOver(stub).SubscribeAsync(Subscriber, "eshop-pro");

        var body = stub.BodyOf(HttpMethod.Post, "/subscriptions.json");
        Assert.NotNull(body);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", body, StringComparison.Ordinal);
        Assert.Contains($"\"customer_id\":{CustomerId}", body, StringComparison.Ordinal);
        Assert.Contains("\"next_billing_at\"", body, StringComparison.Ordinal);

        // No payment fields are sent at all — that is what makes a card-free signup possible.
        Assert.DoesNotContain("credit_card_attributes", body, StringComparison.Ordinal);
        Assert.DoesNotContain("payment_profile", body, StringComparison.Ordinal);
        Assert.DoesNotContain("bank_account_attributes", body, StringComparison.Ordinal);

        // Identifying the customer twice, or by attributes, would create a second customer.
        Assert.DoesNotContain("customer_reference", body, StringComparison.Ordinal);
        Assert.DoesNotContain("customer_attributes", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendsTheShoppersEmailAndAStableReferenceOnTheCustomer()
    {
        var stub = NewShopper().On(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created, SubscriptionJson());

        await ServiceOver(stub).SubscribeAsync(Subscriber, "eshop-pro");

        var body = stub.BodyOf(HttpMethod.Post, "/customers.json");
        Assert.NotNull(body);
        Assert.Contains($"\"email\":\"{SubscriberEmail}\"", body, StringComparison.Ordinal);
        Assert.Contains($"\"reference\":\"{MaxioCustomerReferenceTests.ExpectedReferenceFor(SubscriberEmail)}\"",
            body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReusesAnExistingCustomerInsteadOfCreatingASecond()
    {
        var stub = new MaxioStubHandler().WithFamily().WithSeededPlans()
            .WithExistingCustomer().WithNoSubscriptions()
            .On(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created, SubscriptionJson());

        await ServiceOver(stub).SubscribeAsync(Subscriber, "eshop-pro");

        Assert.Equal(0, stub.CountOf(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public async Task ARepeatSubmitReturnsTheExistingSubscriptionAndCreatesNothing()
    {
        var stub = new MaxioStubHandler().WithFamily().WithSeededPlans()
            .WithExistingCustomer()
            .WithCustomerSubscriptions(SubscriptionListJson());

        var result = await ServiceOver(stub).SubscribeAsync(Subscriber, "eshop-pro");

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, result.Outcome);
        Assert.Equal(900, result.Subscription.Id);
        Assert.Equal(0, stub.CountOf(HttpMethod.Post, "/subscriptions.json"));
        Assert.Equal(0, stub.CountOf(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public async Task ACanceledSubscriptionDoesNotBlockResubscribing()
    {
        var stub = new MaxioStubHandler().WithFamily().WithSeededPlans()
            .WithExistingCustomer()
            .WithCustomerSubscriptions(SubscriptionListJson(state: "canceled"))
            .On(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created, SubscriptionJson(id: 901));

        var result = await ServiceOver(stub).SubscribeAsync(Subscriber, "eshop-pro");

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(901, result.Subscription.Id);
    }

    [Fact]
    public async Task RefusesToDoubleBillAShopperAlreadyOnAnotherPlan()
    {
        var stub = new MaxioStubHandler().WithFamily().WithSeededPlans()
            .WithExistingCustomer()
            .WithCustomerSubscriptions(SubscriptionListJson(handle: "eshop-pro"));

        var ex = await Assert.ThrowsAsync<SubscriptionConflictException>(
            () => ServiceOver(stub).SubscribeAsync(Subscriber, "basic-plan"));

        Assert.Equal("eshop-pro", ex.ExistingPlanHandle);
        Assert.Equal(900, ex.ExistingSubscriptionId);
        Assert.Equal(0, stub.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task RejectsAPlanThatIsNotInTheOfferedCatalog()
    {
        // Scoping signups to the offered plans keeps an archived or foreign-family handle out, and it
        // happens before anything is created.
        var stub = NewShopper();

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => ServiceOver(stub).SubscribeAsync(Subscriber, "retired-plan"));

        Assert.Equal(BillingFailureKind.ProviderRejected, ex.Kind);
        Assert.Equal(404, ex.ProviderStatusCode);
        Assert.Equal(0, stub.CountOf(HttpMethod.Post, "/customers.json"));
        Assert.Equal(0, stub.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SurfacesAProviderValidationRejectionWithItsDetail()
    {
        var stub = NewShopper().On(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.UnprocessableEntity,
            """{"errors":["No payment method was on file for the $299.00 balance"]}""");

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => ServiceOver(stub).SubscribeAsync(Subscriber, "eshop-pro"));

        Assert.Equal(BillingFailureKind.ProviderRejected, ex.Kind);
        Assert.Equal(422, ex.ProviderStatusCode);
        Assert.Contains("No payment method was on file", ex.Message, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------------
    // Write-once: the retry pipeline resends on a transport failure for EVERY verb, so a create that
    // is not guarded can enroll — and bill — the same shopper twice.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task ATransportFailureOnCreateNeverPutsASecondSubscriptionOnTheWire()
    {
        var stub = NewShopper().OnThrow(HttpMethod.Post, "/subscriptions.json",
            new HttpRequestException("connection reset"));

        await Assert.ThrowsAsync<BillingProviderException>(
            () => ServiceOver(stub).SubscribeAsync(Subscriber, "eshop-pro"));

        Assert.Equal(1, stub.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task AnUnconfirmedCreateIsReportedAsUnknownNotAsAFailure()
    {
        // The bytes may have reached the provider. Telling the caller it failed would leave them billed
        // for a subscription we said they do not have.
        var stub = NewShopper().OnThrow(HttpMethod.Post, "/subscriptions.json",
            new HttpRequestException("connection reset"));

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => ServiceOver(stub).SubscribeAsync(Subscriber, "eshop-pro"));

        Assert.Equal(BillingFailureKind.OutcomeUnknown, ex.Kind);
    }

    [Fact]
    public async Task AnUnconfirmedCreateIsSettledByReReadingProviderState()
    {
        // The write did land. Reconciling finds it and the shopper gets their subscription rather than
        // an error that would tempt them into creating a second one.
        var stub = new MaxioStubHandler().WithFamily().WithSeededPlans()
            .WithExistingCustomer()
            .OnSequence(HttpMethod.Get, $"/customers/{CustomerId}/subscriptions.json",
                (HttpStatusCode.OK, "[]"),
                (HttpStatusCode.OK, SubscriptionListJson()))
            .OnThrow(HttpMethod.Post, "/subscriptions.json", new HttpRequestException("connection reset"));

        var result = await ServiceOver(stub).SubscribeAsync(Subscriber, "eshop-pro");

        Assert.Equal(900, result.Subscription.Id);
        Assert.Equal(1, stub.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task ReadsStillRetryOnATransportFailure()
    {
        // The write-once guard must not disarm the SDK's retries for idempotent reads.
        var stub = new MaxioStubHandler().OnThrow(HttpMethod.Get, "/product_families.json",
            new HttpRequestException("connection reset"));

        await Assert.ThrowsAsync<BillingProviderException>(() => ServiceOver(stub).GetPlansAsync());

        Assert.True(stub.CountOf(HttpMethod.Get, "/product_families.json") > 1,
            "a read should be retried by the SDK pipeline");
    }

    [Fact]
    public async Task ConcurrentSubmitsForOneShopperProduceOneSubscription()
    {
        var stub = new MaxioStubHandler().WithFamily().WithSeededPlans()
            .WithExistingCustomer()
            .OnSequence(HttpMethod.Get, $"/customers/{CustomerId}/subscriptions.json",
                (HttpStatusCode.OK, "[]"),
                (HttpStatusCode.OK, SubscriptionListJson()))
            .On(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created, SubscriptionJson());

        var service = ServiceOver(stub);
        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => service.SubscribeAsync(Subscriber, "eshop-pro")));

        Assert.Equal(1, stub.CountOf(HttpMethod.Post, "/subscriptions.json"));
        Assert.Equal(1, results.Count(r => r.Outcome == SubscribeOutcome.Created));
        Assert.All(results, r => Assert.Equal(900, r.Subscription.Id));
    }
}
