using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class SubscriptionOperations
{
    private static BillingCustomer Customer() =>
        new(MaxioJson.CustomerId, MaxioJson.UserReference, MaxioJson.UserReference);

    private static BillingPlan ProPlan() =>
        new(MaxioJson.ProPlanId, "eshop-pro", "Pro Plan", 299.00m, 1, "month");

    [Fact]
    public async Task CreatesASubscriptionForTheCustomerOnTheChosenPlan()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created,
            MaxioJson.Subscription());

        var subscription = await harness.Client.CreateSubscriptionAsync(Customer(), ProPlan());

        Assert.Equal(MaxioJson.SubscriptionId, subscription.Id);
        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.Equal("eshop-pro", subscription.Plan.Handle);
        Assert.Equal(MaxioJson.UserReference, subscription.UserReference);

        var body = harness.Handler.Requests.Single().Body;

        // Enrolment addresses the plan by its durable handle and the customer by the id we resolved.
        Assert.Contains("\"product_handle\":\"eshop-pro\"", body.Replace(" ", string.Empty));
        Assert.Contains($"\"customer_id\":{MaxioJson.CustomerId}", body.Replace(" ", string.Empty));
    }

    [Fact]
    public async Task EnrolsWithInvoiceBillingBecauseEShopOnWebCapturesNoPaymentMethod()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created,
            MaxioJson.Subscription());

        await harness.Client.CreateSubscriptionAsync(Customer(), ProPlan());

        // Without this Maxio refuses the enrolment outright, because there is no payment profile it
        // could charge the first invoice against.
        var body = harness.Handler.Requests.Single().Body.Replace(" ", string.Empty);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", body);
    }

    [Fact]
    public async Task ProjectsTheSubscriptionPeriodAndNextBillingDate()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Get, $"/subscriptions/{MaxioJson.SubscriptionId}.json",
            HttpStatusCode.OK, MaxioJson.Subscription());

        var subscription = await harness.Client.FindSubscriptionByIdAsync(MaxioJson.SubscriptionId);

        Assert.NotNull(subscription);
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(-4)), subscription.CurrentPeriodStartedAt);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(-4)), subscription.CurrentPeriodEndsAt);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(-4)), subscription.NextAssessmentAt);
    }

    [Fact]
    public async Task ConvertsTheOutstandingBalanceFromCents()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Get, $"/subscriptions/{MaxioJson.SubscriptionId}.json",
            HttpStatusCode.OK, MaxioJson.Subscription(balanceInCents: 12_345));

        var subscription = await harness.Client.FindSubscriptionByIdAsync(MaxioJson.SubscriptionId);

        Assert.Equal(123.45m, subscription!.Balance);
    }

    [Fact]
    public async Task ReturnsNullForAnUnknownSubscriptionId()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Get, "/subscriptions/", HttpStatusCode.NotFound, "{}");

        var subscription = await harness.Client.FindSubscriptionByIdAsync(4242);

        // "Not there" is a normal answer; only a genuine failure throws.
        Assert.Null(subscription);
    }

    [Fact]
    public async Task ThrowsRatherThanReturningNullWhenTheReadItselfFails()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Get, "/subscriptions/", HttpStatusCode.Unauthorized, "{}");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => harness.Client.FindSubscriptionByIdAsync(MaxioJson.SubscriptionId));

        Assert.Equal(401, exception.StatusCode);
    }

    [Fact]
    public async Task ListsEverySubscriptionBelongingToACustomer()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Get, $"/customers/{MaxioJson.CustomerId}/subscriptions.json",
            HttpStatusCode.OK,
            MaxioJson.SubscriptionList(
                MaxioJson.Subscription(id: 1),
                MaxioJson.Subscription(id: 2, state: "canceled", product: MaxioJson.BasicPlan())));

        var subscriptions = await harness.Client.ListSubscriptionsForCustomerAsync(Customer());

        Assert.Equal(2, subscriptions.Count);
        Assert.Equal(SubscriptionState.Active, subscriptions[0].State);
        Assert.Equal(SubscriptionState.Canceled, subscriptions[1].State);
        Assert.Equal(29.00m, subscriptions[1].Plan.Price);
    }

    [Fact]
    public async Task ReturnsAnEmptyListForACustomerWithNoSubscriptions()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Get, "/subscriptions.json", HttpStatusCode.OK, MaxioJson.EmptyList);

        var subscriptions = await harness.Client.ListSubscriptionsForCustomerAsync(Customer());

        Assert.Empty(subscriptions);
    }

    [Theory]
    [InlineData("active", SubscriptionState.Active)]
    [InlineData("assessing", SubscriptionState.Active)]
    [InlineData("trialing", SubscriptionState.Trialing)]
    [InlineData("past_due", SubscriptionState.PastDue)]
    [InlineData("soft_failure", SubscriptionState.PastDue)]
    [InlineData("unpaid", SubscriptionState.PastDue)]
    [InlineData("on_hold", SubscriptionState.Paused)]
    [InlineData("paused", SubscriptionState.Paused)]
    [InlineData("canceled", SubscriptionState.Canceled)]
    [InlineData("expired", SubscriptionState.Expired)]
    [InlineData("trial_ended", SubscriptionState.Expired)]
    [InlineData("pending", SubscriptionState.Pending)]
    [InlineData("awaiting_signup", SubscriptionState.Pending)]
    public async Task NormalizesEveryMaxioStateThisIntegrationModels(string wireState, SubscriptionState expected)
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Get, "/subscriptions/", HttpStatusCode.OK,
            MaxioJson.Subscription(state: wireState));

        var subscription = await harness.Client.FindSubscriptionByIdAsync(MaxioJson.SubscriptionId);

        Assert.Equal(expected, subscription!.State);
        // The provider's own name is always preserved so an operator can reconcile it.
        Assert.Equal(wireState, subscription.ProviderState);
    }

    [Fact]
    public async Task MapsAnUnrecognizedStateToUnknownRatherThanGuessing()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Get, "/subscriptions/", HttpStatusCode.OK,
            MaxioJson.Subscription(state: "some_future_state"));

        var subscription = await harness.Client.FindSubscriptionByIdAsync(MaxioJson.SubscriptionId);

        // Maxio's enums are open. Defaulting an unknown state to Active would let a customer act on
        // a subscription that is not actually billing.
        Assert.Equal(SubscriptionState.Unknown, subscription!.State);
        Assert.Equal("some_future_state", subscription.ProviderState);
        Assert.False(subscription.IsActive);
        Assert.False(subscription.CanPause);
    }

    [Fact]
    public async Task FallsBackToANonMatchingReferenceWhenMaxioReportsNoCustomerReference()
    {
        using var harness = MaxioTestHarness.Create();
        var json = MaxioJson.Subscription().Replace(
            $"\"reference\": \"{MaxioJson.UserReference}\",", string.Empty);
        harness.Handler.Respond(HttpMethod.Get, "/subscriptions/", HttpStatusCode.OK, json);

        var subscription = await harness.Client.FindSubscriptionByIdAsync(MaxioJson.SubscriptionId);

        // A customer created outside eShopOnWeb must not accidentally match a signed-in username,
        // or one user could reach another's subscription.
        Assert.NotNull(subscription);
        Assert.NotEqual(MaxioJson.UserReference, subscription.UserReference);
    }

    [Fact]
    public async Task SurfacesAProviderRejectionWhenEnrolmentIsRefused()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.UnprocessableEntity,
            MaxioJson.ErrorList("Product must be specified", "Credit card is required"));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => harness.Client.CreateSubscriptionAsync(Customer(), ProPlan()));

        Assert.Equal(422, exception.StatusCode);
        // The provider's own wording is what tells the customer why they were refused.
        Assert.Contains("Credit card is required", exception.Message);
        Assert.Contains("Product must be specified", exception.ProviderMessages);
    }
}
