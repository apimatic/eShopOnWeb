using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>Customer lookup/creation and subscription reads — the UC1 happy path and its edges.</summary>
public class CustomerAndSubscriptionTests
{
    [Fact]
    public async Task ACustomerIsFoundByTheStableUserReference()
    {
        var (client, handler) = BillingClientFixture.Create(ProviderPayloads.Customer);

        var customer = await client.FindCustomerByReferenceAsync("buyer@example.com");

        Assert.NotNull(customer);
        Assert.Equal(5001, customer!.Id);
        Assert.Equal("buyer@example.com", customer.Reference);
        Assert.Contains("buyer%40example.com", handler.LastRequest.RequestUri!.Query);
    }

    [Fact]
    public async Task AnUnusedReferenceMeansNoCustomerRatherThanAnError()
    {
        var (client, _) = BillingClientFixture.CreateFailing(HttpStatusCode.NotFound, ProviderPayloads.NotFoundError);

        Assert.Null(await client.FindCustomerByReferenceAsync("nobody@example.com"));
    }

    [Fact]
    public async Task BadCredentialsAreNeverReportedAsAnUnusedReference()
    {
        var (client, _) = BillingClientFixture.CreateFailing(HttpStatusCode.Unauthorized);

        // Returning null here would make the caller create a duplicate customer on every request.
        await Assert.ThrowsAsync<BillingProviderException>(
            () => client.FindCustomerByReferenceAsync("buyer@example.com"));
    }

    [Fact]
    public async Task CreatingACustomerSendsTheReferenceThatMakesTheCallIdempotent()
    {
        var (client, handler) = BillingClientFixture.Create(ProviderPayloads.Customer);

        var customer = await client.CreateCustomerAsync("buyer@example.com", "buyer@example.com", "buyer", "eShopOnWeb");

        Assert.Equal(5001, customer.Id);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Contains("\"reference\":\"buyer@example.com\"", handler.LastRequestBody);
        Assert.Contains("\"email\":\"buyer@example.com\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task CreatingACustomerSurfacesAValidationRejection()
    {
        var handler = new StubHttpMessageHandler();
        handler.RespondWith(ProviderPayloads.CustomerValidationError, HttpStatusCode.UnprocessableEntity);
        var client = BillingClientFixture.Build(BillingClientFixture.DefaultSettings(), handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.CreateCustomerAsync("r", "e@example.com", "f", "l"));

        Assert.Equal("CreateCustomer", exception.Operation);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, exception.StatusCode);
        Assert.Contains("is invalid", exception.ProviderMessage);
    }

    [Fact]
    public async Task AnErrorBodyTheSdkCannotParseStillLeavesTheSeamAsAProviderException()
    {
        // The SDK deserialises an error body straight into the operation's declared payload type with
        // no fallback, so a body of the wrong shape escapes as a raw JsonException. That must never
        // reach a caller: the seam promises one exception type for every provider failure.
        var handler = new StubHttpMessageHandler();
        handler.RespondWith("""{"errors": ["a list where an object was declared"]}""",
            HttpStatusCode.UnprocessableEntity);
        var client = BillingClientFixture.Build(BillingClientFixture.DefaultSettings(), handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.CreateCustomerAsync("r", "e@example.com", "f", "l"));

        Assert.Equal("CreateCustomer", exception.Operation);
        Assert.Contains("could not be read", exception.ProviderMessage);
        Assert.IsType<System.Text.Json.JsonException>(exception.InnerException);
    }

    [Fact]
    public async Task ASuccessBodyTheSdkCannotParseAlsoLeavesTheSeamAsAProviderException()
    {
        var handler = new StubHttpMessageHandler();
        handler.RespondWith("""{"subscription": "this should have been an object"}""");
        var client = BillingClientFixture.Build(BillingClientFixture.DefaultSettings(), handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.GetSubscriptionAsync(90001));

        Assert.Equal("GetSubscription", exception.Operation);
    }

    [Fact]
    public async Task CreatingASubscriptionEnrollsTheCustomerInThePlanByHandle()
    {
        var (client, handler) = BillingClientFixture.Create(ProviderPayloads.ActiveSubscription);

        var subscription = await client.CreateSubscriptionAsync(5001, "eshop-pro");

        Assert.Equal(90001, subscription.Id);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal("active", subscription.ProviderState);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal(29900L, subscription.PlanPriceInCents);
        Assert.Equal(299.00m, subscription.PlanPrice);
        Assert.Equal("buyer@example.com", subscription.CustomerReference);
        Assert.True(subscription.IsActive);

        Assert.Contains("\"customer_id\":5001", handler.LastRequestBody);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task ASubscriptionsNextBillingDateIsTheProvidersAssessmentDate()
    {
        var (client, _) = BillingClientFixture.Create(ProviderPayloads.ActiveSubscription);

        var subscription = await client.CreateSubscriptionAsync(5001, "eshop-pro");

        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), subscription.NextBillingDate);
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), subscription.ActivatedAt);
    }

    [Fact]
    public async Task CreatingASubscriptionSurfacesAProviderRejectionWithItsMessage()
    {
        var handler = new StubHttpMessageHandler();
        handler.RespondWith(ProviderPayloads.ValidationError, HttpStatusCode.UnprocessableEntity);
        var client = BillingClientFixture.Build(BillingClientFixture.DefaultSettings(), handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.CreateSubscriptionAsync(5001, "eshop-pro"));

        Assert.Equal("CreateSubscription", exception.Operation);
        Assert.Contains("is invalid", exception.ProviderMessage);
    }

    [Fact]
    public async Task ListingACustomersSubscriptionsUnwrapsEveryEnvelope()
    {
        var (client, _) = BillingClientFixture.Create(
            ProviderPayloads.SubscriptionList(ProviderPayloads.ActiveSubscription, ProviderPayloads.BasicSubscription));

        var subscriptions = (await client.ListSubscriptionsForCustomerAsync(5001)).ToList();

        Assert.Equal(2, subscriptions.Count);
        Assert.Contains(subscriptions, s => s.PlanHandle == "eshop-pro");
        Assert.Contains(subscriptions, s => s.PlanHandle == "basic-plan");
    }

    [Fact]
    public async Task ACustomerWithNoSubscriptionsYieldsAnEmptyCollection()
    {
        var (client, _) = BillingClientFixture.Create(ProviderPayloads.EmptyList);

        Assert.Empty(await client.ListSubscriptionsForCustomerAsync(5001));
    }

    [Fact]
    public async Task ReadingAnUnknownSubscriptionIdYieldsNull()
    {
        var (client, _) = BillingClientFixture.CreateFailing(HttpStatusCode.NotFound, ProviderPayloads.NotFoundError);

        Assert.Null(await client.GetSubscriptionAsync(404404));
    }

    [Fact]
    public async Task ReadingAKnownSubscriptionReturnsItsCurrentState()
    {
        var (client, _) = BillingClientFixture.Create(ProviderPayloads.OnHoldSubscription);

        var subscription = await client.GetSubscriptionAsync(90001);

        Assert.NotNull(subscription);
        Assert.Equal(SubscriptionStatus.OnHold, subscription!.Status);
        Assert.False(subscription.IsActive);
    }

    [Fact]
    public async Task AProviderStateThisApplicationDoesNotModelIsPreservedRatherThanLost()
    {
        const string exoticState = """
            {"subscription": { "id": 90001, "state": "some_future_state",
              "customer": { "id": 5001, "reference": "buyer@example.com" } }}
            """;

        var (client, _) = BillingClientFixture.Create(exoticState);

        var subscription = await client.GetSubscriptionAsync(90001);

        Assert.NotNull(subscription);
        Assert.Equal(SubscriptionStatus.Unknown, subscription!.Status);
        Assert.Equal("some_future_state", subscription.ProviderState);
        Assert.False(subscription.IsActive);
    }

    [Fact]
    public async Task AScheduledEndOfPeriodCancellationIsVisibleOnTheSubscription()
    {
        var (client, _) = BillingClientFixture.Create(ProviderPayloads.PendingCancellationSubscription);

        var subscription = await client.GetSubscriptionAsync(90001);

        Assert.NotNull(subscription);
        Assert.True(subscription!.CancelAtEndOfPeriod);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), subscription.DelayedCancelAt);
    }

    [Fact]
    public async Task AnUnreachableProviderSurfacesAsATypedExceptionRatherThanATransportError()
    {
        var client = BillingClientFixture.Build(BillingClientFixture.DefaultSettings(), new UnreachableHandler());

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => client.GetSubscriptionAsync(90001));

        Assert.Equal("GetSubscription", exception.Operation);
        Assert.Null(exception.StatusCode);
        Assert.Contains("could not be reached", exception.ProviderMessage);
    }

    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException("No such host is known.");
        }
    }
}
