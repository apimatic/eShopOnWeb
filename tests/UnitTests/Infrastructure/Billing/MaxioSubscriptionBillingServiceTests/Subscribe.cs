using System.Net;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.MaxioSubscriptionBillingServiceTests;

public class Subscribe
{
    private static readonly BillingCustomerIdentity Shopper =
        BillingCustomerIdentity.ForUser("demouser@microsoft.com");

    [Fact]
    public async Task CreatesTheCustomerAndTheSubscriptionWhenNeitherExists()
    {
        var (service, handler) = MaxioTestHost.Create(MaxioTestHost.Router(customerExists: false));

        var result = await service.SubscribeAsync(Shopper, MaxioTestHost.PlanHandle);

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(94208636, result.Subscription.Id);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal("Pro Plan", result.Subscription.PlanName);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(299.00m, result.Subscription.Price);
        Assert.Equal("USD", result.Subscription.Currency);
        Assert.NotNull(result.Subscription.NextBillingDate);

        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/customers.json"));
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task IdentifiesThePlanByHandleAndNamesANonCardCollectionMethod()
    {
        var (service, handler) = MaxioTestHost.Create(MaxioTestHost.Router(relationshipInvoicing: true));

        await service.SubscribeAsync(Shopper, MaxioTestHost.PlanHandle);

        var create = Assert.Single(handler.Requests, request =>
            request.Method == HttpMethod.Post && request.Path == "/subscriptions.json");

        Assert.Contains("\"product_handle\":\"eshop-pro\"", create.Body);
        Assert.Contains("\"customer_id\":42", create.Body);

        // Without this, Maxio assesses the first period against a card that this flow never captures and
        // rejects the whole call.
        Assert.Contains("\"payment_collection_method\":\"remittance\"", create.Body);

        // Numeric plan ids are not stable; the handle is.
        Assert.DoesNotContain("\"product_id\"", create.Body);
    }

    [Fact]
    public async Task UsesTheLegacyCollectionMethodOnASiteWithoutRelationshipInvoicing()
    {
        var (service, handler) = MaxioTestHost.Create(MaxioTestHost.Router(relationshipInvoicing: false));

        await service.SubscribeAsync(Shopper, MaxioTestHost.PlanHandle);

        var create = Assert.Single(handler.Requests, request =>
            request.Method == HttpMethod.Post && request.Path == "/subscriptions.json");

        Assert.Contains("\"payment_collection_method\":\"invoice\"", create.Body);
    }

    [Fact]
    public async Task HonoursAConfiguredCollectionMethodOverride()
    {
        var settings = MaxioTestHost.DefaultSettings();
        settings.PaymentCollectionMethod = "automatic";

        var (service, handler) = MaxioTestHost.Create(MaxioTestHost.Router(), settings);

        await service.SubscribeAsync(Shopper, MaxioTestHost.PlanHandle);

        var create = Assert.Single(handler.Requests, request =>
            request.Method == HttpMethod.Post && request.Path == "/subscriptions.json");

        Assert.Contains("\"payment_collection_method\":\"automatic\"", create.Body);
    }

    [Fact]
    public async Task ReusesAnExistingBillingCustomerRatherThanCreatingASecond()
    {
        var (service, handler) = MaxioTestHost.Create(MaxioTestHost.Router(customerExists: true));

        await service.SubscribeAsync(Shopper, MaxioTestHost.PlanHandle);

        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/customers.json"));
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task ReturnsTheExistingSubscriptionInsteadOfEnrollingTwice()
    {
        var (service, handler) = MaxioTestHost.Create(MaxioTestHost.Router(
            customerExists: true,
            existingSubscriptionsJson: MaxioTestHost.LiveSubscriptionListJson));

        var result = await service.SubscribeAsync(Shopper, MaxioTestHost.PlanHandle);

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(94208636, result.Subscription.Id);

        // This is the double-click case: nothing new may be created.
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SerializesConcurrentSubscribesForTheSameShopperSoOnlyOneSubscriptionIsCreated()
    {
        var created = 0;
        var subscriptions = "[]";

        var (service, handler) = MaxioTestHost.Create(request =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/subscriptions.json", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
                return MaxioStubHandler.Json(HttpStatusCode.OK, subscriptions);

            if (path == "/subscriptions.json" && request.Method == HttpMethod.Post)
            {
                Interlocked.Increment(ref created);
                subscriptions = MaxioTestHost.LiveSubscriptionListJson;
                return MaxioStubHandler.Json(HttpStatusCode.Created, MaxioTestHost.CreatedSubscriptionJson);
            }

            return MaxioTestHost.Router(customerExists: true)(request);
        });

        var results = await Task.WhenAll(
            service.SubscribeAsync(Shopper, MaxioTestHost.PlanHandle),
            service.SubscribeAsync(Shopper, MaxioTestHost.PlanHandle));

        Assert.Equal(1, created);
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
        Assert.Single(results, result => !result.AlreadySubscribed);
        Assert.Single(results, result => result.AlreadySubscribed);
        Assert.All(results, result => Assert.Equal(94208636, result.Subscription.Id));
    }

    [Fact]
    public async Task TreatsATerminatedSubscriptionAsResubscribable()
    {
        var (service, handler) = MaxioTestHost.Create(MaxioTestHost.Router(
            customerExists: true,
            existingSubscriptionsJson: MaxioTestHost.CanceledSubscriptionListJson));

        var result = await service.SubscribeAsync(Shopper, MaxioTestHost.PlanHandle);

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task DoesNotResendTheWriteWhenTheConnectionFails()
    {
        // The SDK retries a transport failure on every verb, including POST, and a reset thrown after the
        // bytes arrived is indistinguishable from one thrown before. A resend here would enroll the shopper
        // twice, so the guard has to hold the count at one.
        var (service, handler) = MaxioTestHost.Create(request =>
            request.RequestUri!.AbsolutePath == "/subscriptions.json" && request.Method == HttpMethod.Post
                ? throw new HttpRequestException("connection reset")
                : MaxioTestHost.Router(customerExists: true)(request));

        var exception = await Assert.ThrowsAsync<BillingException>(
            () => service.SubscribeAsync(Shopper, MaxioTestHost.PlanHandle));

        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));

        // The write may have landed, so the caller is told the outcome is unknown rather than "it failed".
        Assert.Equal(BillingFailureKind.OutcomeUnknown, exception.Kind);
    }

    [Fact]
    public async Task AdoptsASubscriptionThatTurnsOutToHaveLandedDespiteTheConnectionFailing()
    {
        var subscriptions = "[]";

        var (service, handler) = MaxioTestHost.Create(request =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path == "/subscriptions.json" && request.Method == HttpMethod.Post)
            {
                // Maxio received and applied the write; the answer never made it back.
                subscriptions = MaxioTestHost.LiveSubscriptionListJson;
                throw new HttpRequestException("connection reset after the request was received");
            }

            if (path.EndsWith("/subscriptions.json", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
                return MaxioStubHandler.Json(HttpStatusCode.OK, subscriptions);

            return MaxioTestHost.Router(customerExists: true)(request);
        });

        var result = await service.SubscribeAsync(Shopper, MaxioTestHost.PlanHandle);

        Assert.Equal(94208636, result.Subscription.Id);
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task DoesNotTreatAServerErrorOnTheCustomerLookupAsAMissingCustomer()
    {
        // Reading "not found" out of a transient failure would create a duplicate billing customer for this
        // shopper on every hiccup.
        var (service, handler) = MaxioTestHost.Create(MaxioTestHost.Router(
            onCustomerLookup: _ => MaxioStubHandler.Json(HttpStatusCode.InternalServerError, "boom")));

        var exception = await Assert.ThrowsAsync<BillingException>(
            () => service.SubscribeAsync(Shopper, MaxioTestHost.PlanHandle));

        Assert.Equal(BillingFailureKind.Unavailable, exception.Kind);
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/customers.json"));
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task TreatsASuccessfulLookupWithNoUsableCustomerAsNotFound()
    {
        var (service, handler) = MaxioTestHost.Create(MaxioTestHost.Router(
            onCustomerLookup: _ => MaxioStubHandler.Json(HttpStatusCode.OK, """{"customer":{"reference":"eshoponweb-demouser@microsoft.com"}}""")));

        await service.SubscribeAsync(Shopper, MaxioTestHost.PlanHandle);

        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public async Task SurfacesTheProvidersValidationMessagesWhenTheSubscribeIsRejected()
    {
        var (service, _) = MaxioTestHost.Create(MaxioTestHost.Router(
            customerExists: true,
            onCreateSubscription: _ => MaxioStubHandler.Json(
                HttpStatusCode.UnprocessableEntity,
                """{"errors":["No payment method was on file for the $299.00 balance"]}""")));

        var exception = await Assert.ThrowsAsync<BillingException>(
            () => service.SubscribeAsync(Shopper, MaxioTestHost.PlanHandle));

        Assert.Equal(BillingFailureKind.InvalidRequest, exception.Kind);
        Assert.Equal(422, exception.ProviderStatusCode);
        Assert.Contains("No payment method was on file for the $299.00 balance", exception.Details);
    }

    [Fact]
    public async Task DoesNotAnswerAnUnreadableErrorBodyAsAServerFailure()
    {
        // The SDK throws while building its error object, destroying the status with it. Answering 5xx would
        // tell a retrying caller to keep retrying a rejection that can never succeed.
        var (service, _) = MaxioTestHost.Create(MaxioTestHost.Router(
            customerExists: true,
            onCreateSubscription: _ => MaxioStubHandler.Json(HttpStatusCode.UnprocessableEntity, """{"unexpected":"shape"}""")));

        var exception = await Assert.ThrowsAsync<BillingException>(
            () => service.SubscribeAsync(Shopper, MaxioTestHost.PlanHandle));

        Assert.Equal(BillingFailureKind.InvalidRequest, exception.Kind);
        Assert.Equal(422, exception.ProviderStatusCode);
    }

    [Fact]
    public async Task RejectsAPlanThatIsNotInTheConfiguredProductFamily()
    {
        var (service, handler) = MaxioTestHost.Create(MaxioTestHost.Router(customerExists: true));

        var exception = await Assert.ThrowsAsync<BillingException>(
            () => service.SubscribeAsync(Shopper, "some-other-plan"));

        Assert.Equal(BillingFailureKind.NotFound, exception.Kind);
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task RejectsAnEmptyPlanHandleWithoutCallingTheProvider()
    {
        var (service, handler) = MaxioTestHost.Create(MaxioTestHost.Router());

        var exception = await Assert.ThrowsAsync<BillingException>(
            () => service.SubscribeAsync(Shopper, "   "));

        Assert.Equal(BillingFailureKind.InvalidRequest, exception.Kind);
        Assert.Empty(handler.Requests);
    }
}
