using System.Net;
using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>Customer and subscription reads and writes (UC1), and the lifecycle actions (UC4).</summary>
public class MaxioBillingClientSubscriptionTests
{
    private const string Reference = "demouser@microsoft.com";
    private const string LookupPath = "customers/lookup.json?reference=demouser@microsoft.com";
    private const int SubscriptionId = 93462813;

    [Fact]
    public async Task EnsureCustomerReusesAnExistingCustomerInsteadOfCreatingASecond()
    {
        var stub = new MaxioApiStub().RespondOk(HttpMethod.Get, LookupPath, MaxioPayloads.CustomerJson);
        var client = BillingClientFixture.Create(stub);

        var customer = await client.EnsureCustomerAsync(Reference, Reference, "demouser", "eShopOnWeb");

        Assert.Equal(14543792, customer.Id);
        Assert.Equal(Reference, customer.Reference);
        // Idempotency is what makes retrying a failed subscribe safe (UC1).
        Assert.Equal(0, stub.CallCount(HttpMethod.Post, "customers.json"));
    }

    [Fact]
    public async Task EnsureCustomerCreatesTheCustomerWithTheEShopUserReferenceWhenAbsent()
    {
        var stub = new MaxioApiStub()
            .Respond(HttpMethod.Get, LookupPath, HttpStatusCode.NotFound, "{\"errors\":[\"Not Found\"]}")
            .RespondOk(HttpMethod.Post, "customers.json", MaxioPayloads.CustomerJson);
        var client = BillingClientFixture.Create(stub);

        await client.EnsureCustomerAsync(Reference, Reference, "demouser", "eShopOnWeb");

        var body = JsonDocument.Parse(stub.LastRequest(HttpMethod.Post, "customers.json")!.Body!)
            .RootElement.GetProperty("customer");

        Assert.Equal(Reference, body.GetProperty("reference").GetString());
        Assert.Equal(Reference, body.GetProperty("email").GetString());
        Assert.Equal("demouser", body.GetProperty("first_name").GetString());
        Assert.Equal("eShopOnWeb", body.GetProperty("last_name").GetString());
    }

    [Fact]
    public async Task FindCustomerByReferenceReturnsNullForAnUnknownUser()
    {
        var stub = new MaxioApiStub().Respond(HttpMethod.Get, "customers/lookup.json?reference=nobody@example.com",
            HttpStatusCode.NotFound, "{\"errors\":[\"Not Found\"]}");
        var client = BillingClientFixture.Create(stub);

        Assert.Null(await client.FindCustomerByReferenceAsync("nobody@example.com"));
    }

    [Fact]
    public async Task CreateSubscriptionSendsThePlanHandleCustomerAndCollectionMethod()
    {
        var stub = new MaxioApiStub().Respond(HttpMethod.Post, "subscriptions.json",
            HttpStatusCode.Created, MaxioPayloads.SubscriptionJson());
        var client = BillingClientFixture.Create(stub);

        await client.CreateSubscriptionAsync(14543792, "eshop-pro");

        var body = JsonDocument.Parse(stub.LastRequest(HttpMethod.Post, "subscriptions.json")!.Body!)
            .RootElement.GetProperty("subscription");

        Assert.Equal("eshop-pro", body.GetProperty("product_handle").GetString());
        Assert.Equal(14543792, body.GetProperty("customer_id").GetInt32());
        // Invoice billing is what lets the demo enrol without capturing a card.
        Assert.Equal("remittance", body.GetProperty("payment_collection_method").GetString());
    }

    [Fact]
    public async Task CreateSubscriptionMapsTheStatePriceAndNextBillingDate()
    {
        var stub = new MaxioApiStub().Respond(HttpMethod.Post, "subscriptions.json",
            HttpStatusCode.Created, MaxioPayloads.SubscriptionJson());
        var client = BillingClientFixture.Create(stub);

        var subscription = await client.CreateSubscriptionAsync(14543792, "eshop-pro");

        Assert.Equal(SubscriptionId, subscription.Id);
        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal(29900L, subscription.PlanPriceInCents);
        Assert.Equal(299.00m, subscription.PlanPrice);
        Assert.Equal(Reference, subscription.CustomerReference);
        Assert.Equal(new DateTimeOffset(2026, 8, 22, 19, 7, 29, TimeSpan.FromHours(5)), subscription.CurrentPeriodEndsAt);
        Assert.True(subscription.IsLive);
    }

    [Theory]
    [InlineData("active", SubscriptionState.Active, true)]
    [InlineData("trialing", SubscriptionState.Trialing, true)]
    [InlineData("on_hold", SubscriptionState.OnHold, false)]
    [InlineData("canceled", SubscriptionState.Canceled, false)]
    [InlineData("past_due", SubscriptionState.PastDue, false)]
    [InlineData("trial_ended", SubscriptionState.TrialEnded, false)]
    [InlineData("expired", SubscriptionState.Expired, false)]
    public async Task ProviderStatesMapOntoTheDomainStates(string providerState, SubscriptionState expected, bool isLive)
    {
        var stub = new MaxioApiStub().RespondOk(HttpMethod.Get, $"subscriptions/{SubscriptionId}.json",
            MaxioPayloads.SubscriptionJson(state: providerState));
        var client = BillingClientFixture.Create(stub);

        var subscription = await client.GetSubscriptionAsync(SubscriptionId);

        Assert.Equal(expected, subscription!.State);
        Assert.Equal(isLive, subscription.IsLive);
    }

    [Fact]
    public async Task AnUnrecognisedProviderStateBecomesUnknownRatherThanBeingTreatedAsLive()
    {
        var stub = new MaxioApiStub().RespondOk(HttpMethod.Get, $"subscriptions/{SubscriptionId}.json",
            MaxioPayloads.SubscriptionJson(state: "something_new"));
        var client = BillingClientFixture.Create(stub);

        var subscription = await client.GetSubscriptionAsync(SubscriptionId);

        Assert.Equal(SubscriptionState.Unknown, subscription!.State);
        Assert.False(subscription.IsLive);
    }

    [Fact]
    public async Task GetSubscriptionReturnsNullForAnUnknownId()
    {
        var stub = new MaxioApiStub().Respond(HttpMethod.Get, "subscriptions/999999999.json",
            HttpStatusCode.NotFound, "{\"errors\":[\"Not Found\"]}");
        var client = BillingClientFixture.Create(stub);

        Assert.Null(await client.GetSubscriptionAsync(999999999));
    }

    [Fact]
    public async Task ListSubscriptionsForCustomerReturnsAnEmptyCollectionForANewCustomer()
    {
        var stub = new MaxioApiStub().RespondOk(HttpMethod.Get, "customers/14543792/subscriptions.json", "[]");
        var client = BillingClientFixture.Create(stub);

        Assert.Empty(await client.ListSubscriptionsForCustomerAsync(14543792));
    }

    [Fact]
    public async Task ListSubscriptionsForCustomerMapsEveryEntry()
    {
        var payload = $"[{MaxioPayloads.SubscriptionJson()}," +
                      $"{MaxioPayloads.SubscriptionJson(id: 93462814, state: "canceled", planHandle: "basic-plan", planName: "Basic Plan", priceInCents: 2900)}]";
        var stub = new MaxioApiStub().RespondOk(HttpMethod.Get, "customers/14543792/subscriptions.json", payload);
        var client = BillingClientFixture.Create(stub);

        var subscriptions = await client.ListSubscriptionsForCustomerAsync(14543792);

        Assert.Equal(2, subscriptions.Count);
        Assert.Equal(299.00m, subscriptions.First().PlanPrice);
        Assert.Equal(29.00m, subscriptions.Last().PlanPrice);
        Assert.Equal(SubscriptionState.Canceled, subscriptions.Last().State);
    }

    [Fact]
    public async Task PauseHoldsTheSubscription()
    {
        var stub = new MaxioApiStub().RespondOk(HttpMethod.Post, $"subscriptions/{SubscriptionId}/hold.json",
            MaxioPayloads.SubscriptionJson(state: "on_hold"));
        var client = BillingClientFixture.Create(stub);

        var subscription = await client.PauseAsync(SubscriptionId, null);

        Assert.Equal(SubscriptionState.OnHold, subscription.State);
    }

    [Fact]
    public async Task PauseSendsTheAutomaticResumeDateWhenOneIsGiven()
    {
        var stub = new MaxioApiStub().RespondOk(HttpMethod.Post, $"subscriptions/{SubscriptionId}/hold.json",
            MaxioPayloads.SubscriptionJson(state: "on_hold"));
        var client = BillingClientFixture.Create(stub);

        await client.PauseAsync(SubscriptionId, new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));

        var body = JsonDocument.Parse(stub.Requests.Single().Body!).RootElement.GetProperty("hold");
        Assert.Contains("2026-09-01", body.GetProperty("automatically_resume_at").GetString());
    }

    [Fact]
    public async Task ResumeReturnsTheSubscriptionToActive()
    {
        var stub = new MaxioApiStub().RespondOk(HttpMethod.Post, $"subscriptions/{SubscriptionId}/resume.json",
            MaxioPayloads.SubscriptionJson(state: "active"));
        var client = BillingClientFixture.Create(stub);

        Assert.Equal(SubscriptionState.Active, (await client.ResumeAsync(SubscriptionId)).State);
    }

    [Fact]
    public async Task ImmediateCancelDeletesTheSubscriptionAndPassesTheReason()
    {
        var stub = new MaxioApiStub().RespondOk(HttpMethod.Delete, $"subscriptions/{SubscriptionId}.json",
            MaxioPayloads.SubscriptionJson(state: "canceled"));
        var client = BillingClientFixture.Create(stub);

        var subscription = await client.CancelAsync(SubscriptionId, CancellationTiming.Immediate, "too expensive");

        Assert.Equal(SubscriptionState.Canceled, subscription.State);
        var body = JsonDocument.Parse(stub.Requests.Single().Body!).RootElement.GetProperty("subscription");
        Assert.Equal("too expensive", body.GetProperty("cancellation_message").GetString());
    }

    [Fact]
    public async Task EndOfPeriodCancelSchedulesTheCancellationAndReportsTheRefreshedState()
    {
        var stub = new MaxioApiStub()
            .RespondOk(HttpMethod.Post, $"subscriptions/{SubscriptionId}/delayed_cancel.json", MaxioPayloads.DelayedCancelJson)
            .RespondOk(HttpMethod.Get, $"subscriptions/{SubscriptionId}.json",
                MaxioPayloads.SubscriptionJson(state: "active", cancelAtEndOfPeriod: true));
        var client = BillingClientFixture.Create(stub);

        var subscription = await client.CancelAsync(SubscriptionId, CancellationTiming.EndOfPeriod, "switching");

        // The delayed-cancel endpoint only acknowledges with a message, so the state must be re-read.
        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.True(subscription.CancelAtEndOfPeriod);
        Assert.Equal(1, stub.CallCount(HttpMethod.Post, $"subscriptions/{SubscriptionId}/delayed_cancel.json"));
        Assert.Equal(1, stub.CallCount(HttpMethod.Get, $"subscriptions/{SubscriptionId}.json"));
    }

    [Fact]
    public async Task ReactivateBringsACancelledSubscriptionBack()
    {
        var stub = new MaxioApiStub().RespondOk(HttpMethod.Put, $"subscriptions/{SubscriptionId}/reactivate.json",
            MaxioPayloads.SubscriptionJson(state: "active"));
        var client = BillingClientFixture.Create(stub);

        Assert.Equal(SubscriptionState.Active, (await client.ReactivateAsync(SubscriptionId)).State);
    }

    [Fact]
    public async Task AProviderRejectionBecomesATypedFailureCarryingTheProviderMessage()
    {
        var stub = new MaxioApiStub().Respond(HttpMethod.Post, $"subscriptions/{SubscriptionId}/hold.json",
            HttpStatusCode.UnprocessableEntity, MaxioPayloads.ErrorListJson);
        var client = BillingClientFixture.Create(stub);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => client.PauseAsync(SubscriptionId, null));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("This subscription is not eligible to be put on hold.", exception.ProviderErrors);
        Assert.Contains("This subscription is not eligible to be put on hold.", exception.Message);
    }

    [Fact]
    public async Task ASingleErrorStringIsSurfacedToo()
    {
        var stub = new MaxioApiStub().Respond(HttpMethod.Delete, $"subscriptions/{SubscriptionId}.json",
            HttpStatusCode.UnprocessableEntity, MaxioPayloads.SingleErrorJson);
        var client = BillingClientFixture.Create(stub);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.CancelAsync(SubscriptionId, CancellationTiming.Immediate, null));

        Assert.Contains("Subscription must be active", exception.ProviderErrors);
    }

    [Fact]
    public async Task AFieldKeyedErrorMapIsFlattenedIntoReadableMessages()
    {
        var stub = new MaxioApiStub()
            .Respond(HttpMethod.Get, LookupPath, HttpStatusCode.NotFound, "{\"errors\":[\"Not Found\"]}")
            .Respond(HttpMethod.Post, "customers.json", HttpStatusCode.UnprocessableEntity, MaxioPayloads.ErrorMapJson);
        var client = BillingClientFixture.Create(stub);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.EnsureCustomerAsync(Reference, Reference, "demouser", "eShopOnWeb"));

        Assert.Contains("customer: Email is invalid", exception.ProviderErrors);
    }

    [Fact]
    public async Task BadCredentialsAreReportedAsSuchRatherThanAsAGenericFailure()
    {
        var stub = new MaxioApiStub().Respond(HttpMethod.Get, $"subscriptions/{SubscriptionId}.json",
            HttpStatusCode.Unauthorized, string.Empty);
        var client = BillingClientFixture.Create(stub);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => client.GetSubscriptionAsync(SubscriptionId));

        Assert.Equal(401, exception.StatusCode);
        Assert.Contains("rejected the configured credentials", exception.Message);
    }
}
