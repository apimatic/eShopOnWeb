using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>Enrolling a customer and reading their subscriptions back.</summary>
public class CustomerAndSubscriptionTests
{
    [Fact]
    public async Task ACustomerIsLookedUpByTheEShopOnWebUserReference()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Get, "/customers/lookup.json", BillingPayloads.Customer);
        var (client, _) = BillingClientFixture.Create(provider);

        var customer = await client.FindCustomerByReferenceAsync(BillingClientFixture.UserReference);

        Assert.NotNull(customer);
        Assert.Equal(88001, customer.Id);
        Assert.Equal(BillingClientFixture.UserReference, customer.Reference);
        Assert.Equal("demouser", customer.FirstName);
        Assert.Contains(provider.Requests, request =>
            request.Uri.Query.Contains("reference=demouser", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnUnknownCustomerReferenceIsNullSoTheCallerCanCreateOne()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Get, "/customers/lookup.json", """{"errors":["Not Found"]}""",
                HttpStatusCode.NotFound);
        var (client, _) = BillingClientFixture.Create(provider);

        Assert.Null(await client.FindCustomerByReferenceAsync(BillingClientFixture.UserReference));
    }

    [Fact]
    public async Task CreatingACustomerSendsTheUserReferenceAsTheIdempotencyKey()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Post, "/customers.json", BillingPayloads.Customer);
        var (client, _) = BillingClientFixture.Create(provider);

        var customer = await client.CreateCustomerAsync(BillingClientFixture.UserReference,
            BillingClientFixture.UserReference, "demouser", "eShopOnWeb");

        Assert.Equal(88001, customer.Id);

        var sent = Assert.Single(provider.Requests, request => request.Method == HttpMethod.Post);
        Assert.Contains("\"reference\":\"demouser@microsoft.com\"", sent.Body);
        Assert.Contains("\"first_name\":\"demouser\"", sent.Body);
    }

    [Fact]
    public async Task CreatingASubscriptionSendsTheCustomerAndThePlanHandle()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Post, "/subscriptions.json", BillingPayloads.ActiveSubscription);
        var (client, _) = BillingClientFixture.Create(provider);

        var subscription = await client.CreateSubscriptionAsync(88001, "eshop-pro");

        var sent = Assert.Single(provider.Requests);
        Assert.Contains("\"customer_id\":88001", sent.Body);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", sent.Body);

        Assert.Equal(15236915, subscription.Id);
        Assert.Equal(BillingSubscriptionState.Active, subscription.State);
        Assert.Equal("active", subscription.RawState);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal(299.00m, subscription.PlanPrice);
        Assert.Equal(12.34m, subscription.Balance);
        Assert.Equal("USD", subscription.Currency);
        Assert.Equal(BillingClientFixture.UserReference, subscription.CustomerReference);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(-4)), subscription.NextAssessmentAt);
        Assert.False(subscription.CancelAtEndOfPeriod);
        Assert.True(subscription.IsLive);
    }

    [Fact]
    public async Task ANumericPlanIdentifierIsSentAsAnIdentifierNotAsAHandle()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Post, "/subscriptions.json", BillingPayloads.ActiveSubscription);
        var (client, _) = BillingClientFixture.Create(provider);

        await client.CreateSubscriptionAsync(88001, "7126957");

        var sent = Assert.Single(provider.Requests);
        Assert.Contains("\"product_id\":7126957", sent.Body);
        Assert.DoesNotContain("\"product_handle\"", sent.Body);
    }

    [Fact]
    public async Task ByDefaultTheProviderDecidesHowPaymentIsCollected()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Post, "/subscriptions.json", BillingPayloads.ActiveSubscription);
        var (client, _) = BillingClientFixture.Create(provider);

        await client.CreateSubscriptionAsync(88001, "eshop-pro");

        Assert.DoesNotContain("payment_collection_method", provider.Requests[0].Body);
    }

    [Fact]
    public async Task InvoiceCollectionLetsACardFreePlanBeSubscribedTo()
    {
        var settings = BillingClientFixture.Settings();
        settings.PaymentCollectionMethod = "remittance";

        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Post, "/subscriptions.json", BillingPayloads.ActiveSubscription);
        var (client, _) = BillingClientFixture.Create(provider, settings);

        await client.CreateSubscriptionAsync(88001, "eshop-pro");

        Assert.Contains("\"payment_collection_method\":\"remittance\"", provider.Requests[0].Body);
    }

    [Fact]
    public async Task AnUnrecognisedCollectionMethodIsAConfigurationFaultNotAWireError()
    {
        var settings = BillingClientFixture.Settings();
        settings.PaymentCollectionMethod = "carrier-pigeon";

        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Post, "/subscriptions.json", BillingPayloads.ActiveSubscription);
        var (client, _) = BillingClientFixture.Create(provider, settings);

        await Assert.ThrowsAsync<ApplicationCore.Exceptions.BillingConfigurationException>(
            () => client.CreateSubscriptionAsync(88001, "eshop-pro"));

        Assert.Empty(provider.Requests);
    }

    [Fact]
    public async Task ReadingAnUnknownSubscriptionIdentifierYieldsNull()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Get, "/subscriptions/404404.json", """{"errors":["Not Found"]}""",
                HttpStatusCode.NotFound);
        var (client, _) = BillingClientFixture.Create(provider);

        Assert.Null(await client.GetSubscriptionAsync(404404));
    }

    [Fact]
    public async Task ACustomerWithNoSubscriptionsGetsAnEmptyList()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Get, "/customers/88001/subscriptions.json", "[]");
        var (client, _) = BillingClientFixture.Create(provider);

        Assert.Empty(await client.ListSubscriptionsForCustomerAsync(88001));
    }

    [Fact]
    public async Task ACustomersSubscriptionsAreListedAgainstTheirIdentifier()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Get, "/customers/88001/subscriptions.json", BillingPayloads.CustomerSubscriptions);
        var (client, _) = BillingClientFixture.Create(provider);

        var subscriptions = await client.ListSubscriptionsForCustomerAsync(88001);

        var subscription = Assert.Single(subscriptions);
        Assert.Equal(15236915, subscription.Id);
        Assert.Equal(299.00m, subscription.PlanPrice);
    }

    [Fact]
    public async Task AStateThisBuildDoesNotKnowIsNeverMistakenForATerminatedSubscription()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Get, "/subscriptions/15236915.json",
                """{"subscription":{"id":15236915,"state":"quantum_superposition"}}""");
        var (client, _) = BillingClientFixture.Create(provider);

        var subscription = await client.GetSubscriptionAsync(15236915);

        Assert.NotNull(subscription);
        Assert.Equal(BillingSubscriptionState.Unknown, subscription.State);
        Assert.Equal("quantum_superposition", subscription.RawState);
        Assert.True(subscription.IsLive);
        Assert.False(subscription.IsTerminated);
    }
}
