using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.SubscriptionServiceTests;

public class SubscribeAsync
{
    private readonly SubscriptionServiceFixture _fixture = new();

    private void ArrangePlanAndCustomer()
    {
        _fixture.BillingClient.FindPlanByHandleAsync("eshop-pro", Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceFixture.ProPlan());
        _fixture.BillingClient.EnsureCustomerAsync(SubscriptionServiceFixture.UserReference,
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceFixture.Customer());
    }

    [Fact]
    public async Task EnrollsTheUserAndAnnouncesTheActivation()
    {
        ArrangePlanAndCustomer();
        _fixture.BillingClient.ListSubscriptionsAsync(Arg.Any<BillingCustomer>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Subscription>());
        _fixture.BillingClient.CreateSubscriptionAsync(Arg.Any<BillingCustomer>(), "eshop-pro",
                Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceFixture.SubscriptionIn(SubscriptionState.Active));

        var subscription = await _fixture.CreateService()
            .SubscribeAsync(SubscriptionServiceFixture.UserReference, "eshop-pro");

        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.Equal("eshop-pro", subscription.Plan.Handle);
        await _fixture.Publisher.Received(1)
            .Publish(Arg.Any<SubscriptionActivated>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsTheExistingSubscriptionInsteadOfEnrollingTwice()
    {
        ArrangePlanAndCustomer();
        var existing = SubscriptionServiceFixture.SubscriptionIn(SubscriptionState.Active);
        _fixture.BillingClient.ListSubscriptionsAsync(Arg.Any<BillingCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new[] { existing });

        var subscription = await _fixture.CreateService()
            .SubscribeAsync(SubscriptionServiceFixture.UserReference, "eshop-pro");

        Assert.Same(existing, subscription);
        // No second enrollment, and no activation announced for something that did not just happen.
        await _fixture.BillingClient.DidNotReceive()
            .CreateSubscriptionAsync(Arg.Any<BillingCustomer>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _fixture.Publisher.DidNotReceive()
            .Publish(Arg.Any<SubscriptionActivated>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnrollsWhenTheOnlyExistingSubscriptionIsNoLongerLive()
    {
        ArrangePlanAndCustomer();
        _fixture.BillingClient.ListSubscriptionsAsync(Arg.Any<BillingCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new[] { SubscriptionServiceFixture.SubscriptionIn(SubscriptionState.Canceled) });
        _fixture.BillingClient.CreateSubscriptionAsync(Arg.Any<BillingCustomer>(), "eshop-pro",
                Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceFixture.SubscriptionIn(SubscriptionState.Active));

        var subscription = await _fixture.CreateService()
            .SubscribeAsync(SubscriptionServiceFixture.UserReference, "eshop-pro");

        Assert.Equal(SubscriptionState.Active, subscription.State);
        await _fixture.BillingClient.Received(1)
            .CreateSubscriptionAsync(Arg.Any<BillingCustomer>(), "eshop-pro", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusesToEnrollAgainstAPlanThatDoesNotResolve()
    {
        _fixture.BillingClient.FindPlanByHandleAsync("ghost-plan", Arg.Any<CancellationToken>())
            .Returns((BillingPlan?)null);

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _fixture.CreateService().SubscribeAsync(SubscriptionServiceFixture.UserReference, "ghost-plan"));

        // Nothing is created against a guessed plan.
        await _fixture.BillingClient.DidNotReceive().EnsureCustomerAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task KeepsTheSubscriptionWhenAnInProcessHandlerFails()
    {
        ArrangePlanAndCustomer();
        _fixture.BillingClient.ListSubscriptionsAsync(Arg.Any<BillingCustomer>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Subscription>());
        _fixture.BillingClient.CreateSubscriptionAsync(Arg.Any<BillingCustomer>(), "eshop-pro",
                Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceFixture.SubscriptionIn(SubscriptionState.Active));
        _fixture.Publisher.Publish(Arg.Any<SubscriptionActivated>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("the confirmation email handler blew up"));

        // Eventing is best-effort: the enrollment stands and the caller still gets its subscription.
        var subscription = await _fixture.CreateService()
            .SubscribeAsync(SubscriptionServiceFixture.UserReference, "eshop-pro");

        Assert.Equal(90210, subscription.ProviderSubscriptionId);
    }

    [Fact]
    public async Task SurfacesAProviderFailureRatherThanSwallowingIt()
    {
        ArrangePlanAndCustomer();
        _fixture.BillingClient.ListSubscriptionsAsync(Arg.Any<BillingCustomer>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Subscription>());
        _fixture.BillingClient.CreateSubscriptionAsync(Arg.Any<BillingCustomer>(), "eshop-pro",
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingProviderException(422, "Payment method required."));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => _fixture.CreateService().SubscribeAsync(SubscriptionServiceFixture.UserReference, "eshop-pro"));

        Assert.Equal(422, exception.StatusCode);
    }
}
