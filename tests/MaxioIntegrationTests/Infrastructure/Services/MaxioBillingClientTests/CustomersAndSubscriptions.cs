using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Infrastructure.Services.MaxioBillingClientTests;

public class CustomersAndSubscriptions
{
    private const string USER_REFERENCE = "demouser@microsoft.com";

    private readonly MaxioBillingClientBuilder _builder = new MaxioBillingClientBuilder();

    [Fact]
    public async Task FindsAnExistingCustomerByTheEShopUserReference()
    {
        _builder.Stub.Respond(HttpMethod.Get,
            "/customers/lookup.json?reference=demouser%40microsoft.com",
            MaxioPayloads.Customer(55501, USER_REFERENCE, USER_REFERENCE));

        var customer = await _builder.Build().FindCustomerByReferenceAsync(USER_REFERENCE);

        Assert.NotNull(customer);
        Assert.Equal(55501, customer.Id);
        Assert.Equal(USER_REFERENCE, customer.Reference);
        Assert.Equal(USER_REFERENCE, customer.Email);
    }

    [Fact]
    public async Task ReturnsNullWhenTheUserHasNoCustomerRecordYet()
    {
        _builder.Stub.RespondWithFailure(HttpMethod.Get,
            "/customers/lookup.json?reference=nobody%40microsoft.com", HttpStatusCode.NotFound, "{}");

        Assert.Null(await _builder.Build().FindCustomerByReferenceAsync("nobody@microsoft.com"));
    }

    [Fact]
    public async Task CreatesACustomerKeyedOnTheEShopUserReference()
    {
        _builder.Stub.Respond(HttpMethod.Post, "/customers.json",
            MaxioPayloads.Customer(55501, USER_REFERENCE, USER_REFERENCE));

        var customer = await _builder.Build().CreateCustomerAsync(USER_REFERENCE, USER_REFERENCE);

        Assert.Equal(55501, customer.Id);

        using var body = JsonDocument.Parse(_builder.Stub.LastRequest.Body!);
        var sent = body.RootElement.GetProperty("customer");
        Assert.Equal(USER_REFERENCE, sent.GetProperty("reference").GetString());
        Assert.Equal(USER_REFERENCE, sent.GetProperty("email").GetString());

        // The provider requires both names, so neither may be sent blank.
        Assert.False(string.IsNullOrWhiteSpace(sent.GetProperty("first_name").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(sent.GetProperty("last_name").GetString()));
    }

    [Fact]
    public async Task SurfacesAValidationRejectionWhenCreatingACustomer()
    {
        _builder.Stub.RespondWithFailure(HttpMethod.Post, "/customers.json",
            HttpStatusCode.UnprocessableEntity, MaxioPayloads.ErrorMap("customer", "can't be blank"));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => _builder.Build().CreateCustomerAsync(USER_REFERENCE, USER_REFERENCE));

        Assert.Equal(422, exception.StatusCode);
        Assert.Equal("customer: can't be blank", Assert.Single(exception.ProviderErrors));
    }

    [Fact]
    public async Task EnrollsACustomerInAPlanByHandle()
    {
        _builder.Stub.Respond(HttpMethod.Post, "/subscriptions.json",
            MaxioPayloads.SubscriptionEnvelope(MaxioPayloads.Subscription(15236915, "active", "eshop-pro",
                "Pro Plan", MaxioPayloads.PRO_PLAN_CENTS)),
            HttpStatusCode.Created);

        var subscription = await _builder.Build().CreateSubscriptionAsync(USER_REFERENCE, "eshop-pro");

        Assert.Equal(15236915, subscription.Id);
        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal("Pro Plan", subscription.PlanName);
        Assert.Equal(299.00m, subscription.PlanPrice);
        Assert.True(subscription.IsActive);

        using var body = JsonDocument.Parse(_builder.Stub.LastRequest.Body!);
        var sent = body.RootElement.GetProperty("subscription");
        Assert.Equal("eshop-pro", sent.GetProperty("product_handle").GetString());
        Assert.Equal(USER_REFERENCE, sent.GetProperty("customer_reference").GetString());
    }

    [Fact]
    public async Task InvoicesTheSubscriptionSoEnrollingNeedsNoPaymentMethod()
    {
        _builder.Stub.Respond(HttpMethod.Post, "/subscriptions.json",
            MaxioPayloads.SubscriptionEnvelope(MaxioPayloads.Subscription(15236915, "active", "eshop-pro",
                "Pro Plan", MaxioPayloads.PRO_PLAN_CENTS)),
            HttpStatusCode.Created);

        await _builder.Build().CreateSubscriptionAsync(USER_REFERENCE, "eshop-pro");

        using var body = JsonDocument.Parse(_builder.Stub.LastRequest.Body!);
        Assert.Equal(MaxioSettings.REMITTANCE_COLLECTION,
            body.RootElement.GetProperty("subscription").GetProperty("payment_collection_method").GetString());
    }

    [Fact]
    public async Task LetsTheProviderApplyItsOwnDefaultWhenNoCollectionMethodIsConfigured()
    {
        _builder.WithSettings(new MaxioSettings
        {
            ApiKey = MaxioBillingClientBuilder.TEST_API_KEY,
            Subdomain = MaxioBillingClientBuilder.TEST_SUBDOMAIN,
            PaymentCollectionMethod = string.Empty
        });
        _builder.Stub.Respond(HttpMethod.Post, "/subscriptions.json",
            MaxioPayloads.SubscriptionEnvelope(MaxioPayloads.Subscription(15236915, "active", "eshop-pro",
                "Pro Plan", MaxioPayloads.PRO_PLAN_CENTS)),
            HttpStatusCode.Created);

        await _builder.Build().CreateSubscriptionAsync(USER_REFERENCE, "eshop-pro");

        using var body = JsonDocument.Parse(_builder.Stub.LastRequest.Body!);
        Assert.False(body.RootElement.GetProperty("subscription")
            .TryGetProperty("payment_collection_method", out _));
    }

    [Fact]
    public async Task SurfacesAnEnrollmentRejectionFromTheProvider()
    {
        _builder.Stub.RespondWithFailure(HttpMethod.Post, "/subscriptions.json",
            HttpStatusCode.UnprocessableEntity,
            MaxioPayloads.ErrorList("Payment profile is required", "Credit card is not valid"));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => _builder.Build().CreateSubscriptionAsync(USER_REFERENCE, "eshop-pro"));

        Assert.Equal(422, exception.StatusCode);
        Assert.Equal(new[] { "Payment profile is required", "Credit card is not valid" }, exception.ProviderErrors);
        Assert.Contains("Payment profile is required", exception.Message);
    }

    [Fact]
    public async Task ListsEverySubscriptionBelongingToACustomer()
    {
        _builder.Stub.Respond(HttpMethod.Get, "/customers/55501/subscriptions.json",
            MaxioPayloads.SubscriptionList(
                MaxioPayloads.Subscription(15236915, "active", "eshop-pro", "Pro Plan", MaxioPayloads.PRO_PLAN_CENTS),
                MaxioPayloads.Subscription(15236916, "canceled", "basic-plan", "Basic Plan",
                    MaxioPayloads.BASIC_PLAN_CENTS)));

        var subscriptions = await _builder.Build().ListSubscriptionsForCustomerAsync(55501);

        Assert.Equal(2, subscriptions.Count);
        Assert.Single(subscriptions, subscription => subscription.IsActive);
        Assert.Equal(SubscriptionState.Canceled, subscriptions.Last().State);
        Assert.Equal(29.00m, subscriptions.Last().PlanPrice);
    }

    [Fact]
    public async Task ReturnsAnEmptyCollectionForACustomerWithNoSubscriptions()
    {
        _builder.Stub.Respond(HttpMethod.Get, "/customers/55501/subscriptions.json", "[]");

        var subscriptions = await _builder.Build().ListSubscriptionsForCustomerAsync(55501);

        Assert.NotNull(subscriptions);
        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task ReadsASubscriptionIncludingItsPendingCancellationAndScheduledPlanChange()
    {
        _builder.Stub.Respond(HttpMethod.Get, "/subscriptions/15236915.json",
            MaxioPayloads.SubscriptionEnvelope(MaxioPayloads.Subscription(15236915, "active", "eshop-pro",
                "Pro Plan", MaxioPayloads.PRO_PLAN_CENTS,
                cancelAtEndOfPeriod: true,
                delayedCancelAt: "2026-08-23T12:00:00-05:00",
                nextProductHandle: "basic-plan")));

        var subscription = await _builder.Build().GetSubscriptionAsync(15236915);

        Assert.NotNull(subscription);
        Assert.True(subscription.CancelAtEndOfPeriod);
        Assert.Equal("basic-plan", subscription.NextPlanHandle);
        Assert.Equal(new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.FromHours(-5)), subscription.DelayedCancelAt);
        Assert.Equal(new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.FromHours(-5)),
            subscription.CurrentPeriodEndsAt);
        Assert.Equal(55501, subscription.CustomerId);
        Assert.Equal(USER_REFERENCE, subscription.CustomerReference);
    }

    [Fact]
    public async Task ReturnsNullForAnUnknownSubscriptionId()
    {
        _builder.Stub.RespondWithFailure(HttpMethod.Get, "/subscriptions/999999.json",
            HttpStatusCode.NotFound, "{}");

        Assert.Null(await _builder.Build().GetSubscriptionAsync(999999));
    }

    [Theory]
    [InlineData("active", SubscriptionState.Active)]
    [InlineData("on_hold", SubscriptionState.OnHold)]
    [InlineData("canceled", SubscriptionState.Canceled)]
    [InlineData("trialing", SubscriptionState.Trialing)]
    [InlineData("past_due", SubscriptionState.PastDue)]
    [InlineData("expired", SubscriptionState.Expired)]
    [InlineData("something_new", SubscriptionState.Unknown)]
    public async Task MapsEveryProviderStateOntoTheDomainState(string providerState, SubscriptionState expected)
    {
        _builder.Stub.Respond(HttpMethod.Get, "/subscriptions/15236915.json",
            MaxioPayloads.SubscriptionEnvelope(MaxioPayloads.Subscription(15236915, providerState, "eshop-pro",
                "Pro Plan", MaxioPayloads.PRO_PLAN_CENTS)));

        var subscription = await _builder.Build().GetSubscriptionAsync(15236915);

        Assert.Equal(expected, subscription!.State);
    }

    [Fact]
    public async Task TreatsOnlyActiveAndTrialingSubscriptionsAsLive()
    {
        _builder.Stub.Respond(HttpMethod.Get, "/customers/55501/subscriptions.json",
            MaxioPayloads.SubscriptionList(
                MaxioPayloads.Subscription(1, "on_hold", "eshop-pro", "Pro Plan", MaxioPayloads.PRO_PLAN_CENTS),
                MaxioPayloads.Subscription(2, "past_due", "eshop-pro", "Pro Plan", MaxioPayloads.PRO_PLAN_CENTS),
                MaxioPayloads.Subscription(3, "trialing", "eshop-pro", "Pro Plan", MaxioPayloads.PRO_PLAN_CENTS)));

        var subscriptions = await _builder.Build().ListSubscriptionsForCustomerAsync(55501);

        Assert.Equal(new[] { 3 }, subscriptions.Where(s => s.IsActive).Select(s => s.Id));
    }
}
