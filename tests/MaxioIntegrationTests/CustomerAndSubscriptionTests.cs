using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Infrastructure;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// UC1 — the idempotent customer link and the enrolment itself.
/// </summary>
public class CustomerAndSubscriptionTests
{
    private const string Reference = "demouser@microsoft.com";

    [Fact]
    public async Task Reuses_an_existing_customer_instead_of_creating_a_second_one()
    {
        var server = new StubBillingServer()
            .Get("customers/lookup", BillingJson.Customer(501, Reference));

        var customer = await BillingTestHarness.Build(server)
            .EnsureCustomerAsync(Reference, "Demo", "User", Reference);

        Assert.Equal(501, customer.Id);
        Assert.Equal(Reference, customer.Reference);

        // Nothing was created: idempotency on the user reference is the whole point.
        Assert.Empty(server.Requests.Where(r => r.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task Creates_the_customer_when_the_reference_is_unknown()
    {
        var server = new StubBillingServer()
            .Get("customers/lookup", BillingJson.NotFound(), HttpStatusCode.NotFound)
            .Post("/customers.json", BillingJson.Customer(777, Reference));

        var customer = await BillingTestHarness.Build(server)
            .EnsureCustomerAsync(Reference, "Demo", "User", Reference);

        Assert.Equal(777, customer.Id);

        var created = Assert.Single(server.RequestsFor("/customers.json").Where(r => r.Method == HttpMethod.Post));
        Assert.Contains("\"reference\":", created.Body, StringComparison.Ordinal);
        Assert.Contains(Reference, created.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recovers_when_another_writer_created_the_same_customer_concurrently()
    {
        var server = new StubBillingServer()
            // First lookup misses, so a create is attempted...
            .Get("customers/lookup", BillingJson.NotFound(), HttpStatusCode.NotFound)
            // ...the create loses the race...
            .Post("/customers.json", BillingJson.Errors("reference: has already been taken"), HttpStatusCode.UnprocessableEntity)
            // ...and the second lookup finds what the winner created.
            .Get("customers/lookup", BillingJson.Customer(888, Reference));

        var customer = await BillingTestHarness.Build(server)
            .EnsureCustomerAsync(Reference, "Demo", "User", Reference);

        Assert.Equal(888, customer.Id);
    }

    [Fact]
    public async Task Surfaces_a_customer_creation_failure_that_is_not_a_race()
    {
        var server = new StubBillingServer()
            .Get("customers/lookup", BillingJson.NotFound(), HttpStatusCode.NotFound)
            .Post("/customers.json", BillingJson.Errors("Email: is invalid."), HttpStatusCode.UnprocessableEntity);

        await Assert.ThrowsAsync<BillingProviderException>(() => BillingTestHarness.Build(server)
            .EnsureCustomerAsync(Reference, "Demo", "User", "not-an-email"));
    }

    [Fact]
    public async Task Creates_a_subscription_by_handle_and_reference_without_any_payment_details()
    {
        var server = new StubBillingServer()
            .Post("/subscriptions.json", BillingJson.SubscriptionEnvelope(BillingJson.Subscription(1001)));

        var subscription = await BillingTestHarness.Build(server)
            .CreateSubscriptionAsync(Reference, "eshop-pro");

        Assert.Equal(1001, subscription.Id);
        Assert.Equal(SubscriptionLifecycleState.Active, subscription.State);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal(299.00m, subscription.PlanPrice);
        Assert.True(subscription.IsBillable);
        Assert.NotNull(subscription.NextBillingDate);

        var body = Assert.Single(server.RequestsFor("/subscriptions.json")).Body;
        Assert.Contains("\"product_handle\":\"eshop-pro\"", body, StringComparison.Ordinal);
        Assert.Contains("\"customer_reference\":", body, StringComparison.Ordinal);
        // No card capture is attempted: the demo plans do not require a payment method.
        Assert.DoesNotContain("credit_card", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payment_profile", body, StringComparison.OrdinalIgnoreCase);

        // With no payment profile the subscription must be invoiced rather than auto-collected, or the
        // provider refuses it for having no payment method on file.
        Assert.Contains("\"payment_collection_method\":\"remittance\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Surfaces_the_providers_own_message_when_enrolment_is_rejected()
    {
        var server = new StubBillingServer()
            .Post("/subscriptions.json",
                BillingJson.Errors("Product requires a credit card.", "Customer must have a payment profile."),
                HttpStatusCode.UnprocessableEntity);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingTestHarness.Build(server).CreateSubscriptionAsync(Reference, "eshop-pro"));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("Product requires a credit card.", exception.ProviderMessage, StringComparison.Ordinal);
        Assert.Contains("Customer must have a payment profile.", exception.ProviderMessage, StringComparison.Ordinal);
        Assert.Equal("CreateSubscriptionAsync", exception.Operation);
    }

    [Fact]
    public async Task Lists_the_subscriptions_belonging_to_a_customer()
    {
        var server = new StubBillingServer()
            .Get("customers/lookup", BillingJson.Customer(501, Reference))
            .Get("/subscriptions.json", BillingJson.SubscriptionList(
                BillingJson.Subscription(1001, planHandle: "eshop-pro"),
                BillingJson.Subscription(1002, state: "canceled", planHandle: "basic-plan", productPriceInCents: 2900)));

        var subscriptions = (await BillingTestHarness.Build(server).ListSubscriptionsAsync(Reference)).ToList();

        Assert.Equal(2, subscriptions.Count);
        Assert.Equal(SubscriptionLifecycleState.Active, subscriptions[0].State);
        Assert.Equal(SubscriptionLifecycleState.Canceled, subscriptions[1].State);
        Assert.Equal(29.00m, subscriptions[1].PlanPrice);
        Assert.False(subscriptions[1].IsBillable);
    }

    [Fact]
    public async Task Returns_no_subscriptions_for_a_customer_the_provider_has_never_seen()
    {
        var server = new StubBillingServer()
            .Get("customers/lookup", BillingJson.NotFound(), HttpStatusCode.NotFound);

        var subscriptions = await BillingTestHarness.Build(server).ListSubscriptionsAsync("nobody@example.com");

        // A shopper who never subscribed is an empty list, not an error.
        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task Returns_null_for_an_unknown_subscription_id()
    {
        var server = new StubBillingServer()
            .Get("/subscriptions/4242.json", BillingJson.NotFound(), HttpStatusCode.NotFound);

        var subscription = await BillingTestHarness.Build(server).GetSubscriptionAsync(4242);

        Assert.Null(subscription);
    }

    [Theory]
    [InlineData("active", SubscriptionLifecycleState.Active)]
    [InlineData("trialing", SubscriptionLifecycleState.Trialing)]
    [InlineData("on_hold", SubscriptionLifecycleState.Paused)]
    [InlineData("paused", SubscriptionLifecycleState.Paused)]
    [InlineData("canceled", SubscriptionLifecycleState.Canceled)]
    [InlineData("past_due", SubscriptionLifecycleState.PastDue)]
    [InlineData("expired", SubscriptionLifecycleState.Expired)]
    [InlineData("trial_ended", SubscriptionLifecycleState.TrialEnded)]
    [InlineData("unpaid", SubscriptionLifecycleState.Unpaid)]
    [InlineData("failed_to_create", SubscriptionLifecycleState.Failed)]
    [InlineData("something_new_from_the_provider", SubscriptionLifecycleState.Unknown)]
    public async Task Maps_provider_states_onto_the_domain_lifecycle(string wireState, SubscriptionLifecycleState expected)
    {
        var server = new StubBillingServer()
            .Get("/subscriptions/1001.json", BillingJson.SubscriptionEnvelope(
                BillingJson.Subscription(1001, state: wireState)));

        var subscription = await BillingTestHarness.Build(server).GetSubscriptionAsync(1001);

        Assert.NotNull(subscription);
        Assert.Equal(expected, subscription!.State);
        // The raw value is preserved so an unmodelled state is still diagnosable.
        Assert.Equal(wireState, subscription.ProviderState);
    }
}
