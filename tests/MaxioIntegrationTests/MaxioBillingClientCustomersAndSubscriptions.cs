using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Customer idempotency and subscription reads/writes — the heart of UC1.
/// </summary>
public class MaxioBillingClientCustomersAndSubscriptions
{
    private const string Reference = "demouser@microsoft.com";

    private static BillingCustomerDetails Details => new()
    {
        Reference = Reference,
        Email = Reference,
        FirstName = "demouser",
        LastName = "Customer"
    };

    private static Func<Uri, bool> CustomerLookup => MaxioApiStub.PathContaining("customers", "lookup");

    [Fact]
    public async Task EnsureCustomerReusesTheExistingCustomerAndCreatesNothing()
    {
        var stub = new MaxioApiStub()
            .Respond(HttpMethod.Get, CustomerLookup, HttpStatusCode.OK,
                MaxioJson.CustomerResponse(MaxioJson.Customer()));

        using var harness = new MaxioTestHarness(stub);

        var customer = await harness.Client.EnsureCustomerAsync(Details);

        Assert.Equal(55001, customer.Id);
        Assert.Equal(Reference, customer.Reference);
        // Idempotency is the point: a second customer must never be created for the same user.
        Assert.DoesNotContain(stub.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task EnsureCustomerCreatesTheCustomerWithTheUserReferenceWhenNoneExists()
    {
        var stub = new MaxioApiStub()
            .Respond(HttpMethod.Get, CustomerLookup, HttpStatusCode.NotFound, MaxioJson.ErrorList("Not found"))
            .Respond(HttpMethod.Post, MaxioApiStub.PathEndingWith("customers.json"), HttpStatusCode.Created,
                MaxioJson.CustomerResponse(MaxioJson.Customer()));

        using var harness = new MaxioTestHarness(stub);

        var customer = await harness.Client.EnsureCustomerAsync(Details);

        Assert.Equal(55001, customer.Id);

        var created = Assert.Single(stub.Requests, r => r.Method == HttpMethod.Post);
        // The eShopOnWeb identity must be sent as the provider-side reference, or repeat
        // subscribes would silently create duplicate customers.
        Assert.Contains("\"reference\"", created.Body, StringComparison.Ordinal);
        Assert.Contains(Reference, created.Body, StringComparison.Ordinal);
        Assert.Contains("\"first_name\"", created.Body, StringComparison.Ordinal);
        Assert.Contains("\"last_name\"", created.Body, StringComparison.Ordinal);
        Assert.Contains("\"email\"", created.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnsureCustomerRecoversWhenAConcurrentCallWonTheRaceToCreate()
    {
        // First lookup misses, the create loses a race with 422, and the retry lookup now finds it.
        var stub = new MaxioApiStub()
            .RespondInSequence(HttpMethod.Get, CustomerLookup,
                (HttpStatusCode.NotFound, MaxioJson.ErrorList("Not found")),
                (HttpStatusCode.OK, MaxioJson.CustomerResponse(MaxioJson.Customer())))
            .Respond(HttpMethod.Post, MaxioApiStub.PathEndingWith("customers.json"),
                HttpStatusCode.UnprocessableEntity, MaxioJson.ErrorList("Reference: must be unique."));

        using var harness = new MaxioTestHarness(stub);

        var customer = await harness.Client.EnsureCustomerAsync(Details);

        Assert.Equal(55001, customer.Id);
    }

    [Fact]
    public async Task EnsureCustomerSurfacesACreateFailureAsATypedProviderException()
    {
        var stub = new MaxioApiStub()
            .Respond(HttpMethod.Get, CustomerLookup, HttpStatusCode.NotFound, MaxioJson.ErrorList("Not found"))
            .Respond(HttpMethod.Post, MaxioApiStub.PathEndingWith("customers.json"),
                HttpStatusCode.UnprocessableEntity, MaxioJson.ErrorList("Email: is invalid."));

        using var harness = new MaxioTestHarness(stub);

        // The SDK's generated 422 model for customer writes does not match what Maxio really
        // sends, so it throws a JsonException while building its own error object and never
        // raises an SdkException. The integration must still fail as one typed provider error
        // rather than letting a raw serializer exception escape into the host.
        var ex = await Assert.ThrowsAnyAsync<BillingProviderException>(
            () => harness.Client.EnsureCustomerAsync(Details));

        Assert.Equal("EnsureCustomerAsync", ex.Operation);
        Assert.IsNotType<System.Text.Json.JsonException>(ex);
    }

    [Fact]
    public async Task FindCustomerByReferenceReturnsNullWhenTheProviderHasNoSuchCustomer()
    {
        var stub = new MaxioApiStub()
            .Respond(HttpMethod.Get, CustomerLookup, HttpStatusCode.NotFound, MaxioJson.ErrorList("Not found"));

        using var harness = new MaxioTestHarness(stub);

        Assert.Null(await harness.Client.FindCustomerByReferenceAsync("nobody@example.com"));
    }

    [Fact]
    public async Task CreateSubscriptionSendsTheResolvedProductIdAndMapsTheResult()
    {
        var stub = MaxioTestHarness.StubCatalog(new MaxioApiStub())
            .Respond(HttpMethod.Post, MaxioApiStub.PathEndingWith("subscriptions.json"), HttpStatusCode.Created,
                MaxioJson.SubscriptionResponse(MaxioJson.Subscription()));

        using var harness = new MaxioTestHarness(stub);

        var subscription = await harness.Client.CreateSubscriptionAsync(55001, "eshop-pro");

        Assert.Equal(88001, subscription.Id);
        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.True(subscription.IsActive);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal(299.00m, subscription.PlanPrice);
        Assert.Equal(55001, subscription.CustomerId);
        Assert.Equal(Reference, subscription.CustomerReference);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(-4)), subscription.NextAssessmentAt);

        // Enrolment goes by the id resolved inside the configured family, not by the site-wide
        // handle, so it cannot land on a same-handle plan in another family.
        var body = Assert.Single(stub.Requests, r => r.Method == HttpMethod.Post).Body;
        Assert.Contains("\"product_id\"", body, StringComparison.Ordinal);
        Assert.Contains("7130997", body, StringComparison.Ordinal);
        Assert.Contains("\"customer_id\"", body, StringComparison.Ordinal);
        Assert.Contains("55001", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateSubscriptionRefusesAPlanHandleThatIsNotInTheConfiguredFamily()
    {
        var stub = MaxioTestHarness.StubCatalog(new MaxioApiStub());

        using var harness = new MaxioTestHarness(stub);

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => harness.Client.CreateSubscriptionAsync(55001, "not-in-this-family"));

        // Nothing may be enrolled when the plan cannot be resolved.
        Assert.DoesNotContain(stub.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task CreateSubscriptionSurfacesTheProvidersValidationMessages()
    {
        var stub = MaxioTestHarness.StubCatalog(new MaxioApiStub())
            .Respond(HttpMethod.Post, MaxioApiStub.PathEndingWith("subscriptions.json"),
                HttpStatusCode.UnprocessableEntity,
                MaxioJson.ErrorList("Credit card is required.", "Product must be specified."));

        using var harness = new MaxioTestHarness(stub);

        var ex = await Assert.ThrowsAsync<BillingProviderValidationException>(
            () => harness.Client.CreateSubscriptionAsync(55001, "eshop-pro"));

        Assert.Equal(422, ex.StatusCode);
        Assert.Equal(2, ex.Errors.Count);
        Assert.Contains("Credit card is required.", ex.Errors);
        Assert.Contains("Product must be specified.", ex.Errors);
        Assert.Equal("CreateSubscriptionAsync", ex.Operation);
    }

    [Fact]
    public async Task FindSubscriptionReturnsNullForAnUnknownId()
    {
        using var harness = new MaxioTestHarness(new MaxioApiStub());

        Assert.Null(await harness.Client.FindSubscriptionAsync(999999));
    }

    [Fact]
    public async Task FindSubscriptionMapsAHoldToThePausedDomainState()
    {
        // Maxio reports a hold as on_hold; the domain calls that Paused.
        var stub = new MaxioApiStub()
            .Respond(HttpMethod.Get, MaxioApiStub.PathContaining("subscriptions/88001"), HttpStatusCode.OK,
                MaxioJson.SubscriptionResponse(MaxioJson.Subscription(state: "on_hold")));

        using var harness = new MaxioTestHarness(stub);

        var subscription = await harness.Client.FindSubscriptionAsync(88001);

        Assert.Equal(SubscriptionState.Paused, subscription!.State);
        Assert.Equal("on_hold", subscription.ProviderState);
        Assert.False(subscription.IsActive);
    }

    [Theory]
    [InlineData("active", SubscriptionState.Active)]
    [InlineData("trialing", SubscriptionState.Trialing)]
    [InlineData("past_due", SubscriptionState.PastDue)]
    [InlineData("canceled", SubscriptionState.Canceled)]
    [InlineData("expired", SubscriptionState.Expired)]
    [InlineData("on_hold", SubscriptionState.Paused)]
    [InlineData("paused", SubscriptionState.Paused)]
    [InlineData("unpaid", SubscriptionState.Unpaid)]
    [InlineData("trial_ended", SubscriptionState.TrialEnded)]
    [InlineData("failed_to_create", SubscriptionState.Failed)]
    [InlineData("something_new", SubscriptionState.Unknown)]
    public async Task SubscriptionStatesMapOntoTheDomainVocabulary(string providerState, SubscriptionState expected)
    {
        var stub = new MaxioApiStub()
            .Respond(HttpMethod.Get, MaxioApiStub.PathContaining("subscriptions/88001"), HttpStatusCode.OK,
                MaxioJson.SubscriptionResponse(MaxioJson.Subscription(state: providerState)));

        using var harness = new MaxioTestHarness(stub);

        Assert.Equal(expected, (await harness.Client.FindSubscriptionAsync(88001))!.State);
    }

    [Fact]
    public async Task ListSubscriptionsForCustomerMapsEveryEntry()
    {
        var stub = new MaxioApiStub()
            .Respond(HttpMethod.Get, MaxioApiStub.PathContaining("customers", "subscriptions"), HttpStatusCode.OK,
                MaxioJson.SubscriptionList(
                    MaxioJson.Subscription(id: 88001, state: "active"),
                    MaxioJson.Subscription(id: 88002, state: "canceled", canceledAt: "2026-06-01T00:00:00-04:00")));

        using var harness = new MaxioTestHarness(stub);

        var subscriptions = await harness.Client.ListSubscriptionsForCustomerAsync(55001);

        Assert.Equal(2, subscriptions.Count);
        Assert.Equal(SubscriptionState.Active, subscriptions[0].State);
        Assert.Equal(SubscriptionState.Canceled, subscriptions[1].State);
        Assert.NotNull(subscriptions[1].CanceledAt);
    }

    [Fact]
    public async Task ListSubscriptionsForCustomerReturnsAnEmptyListWhenThereAreNone()
    {
        var stub = new MaxioApiStub()
            .Respond(HttpMethod.Get, MaxioApiStub.PathContaining("customers", "subscriptions"),
                HttpStatusCode.OK, "[]");

        using var harness = new MaxioTestHarness(stub);

        Assert.Empty(await harness.Client.ListSubscriptionsForCustomerAsync(55001));
    }
}
