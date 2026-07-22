using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.SubscriptionServiceTests;

public class SubscribeAsync
{
    private readonly IBillingClient _billingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();

    private SubscriptionService Service => new(_billingClient, _publisher, _logger);

    public SubscribeAsync()
    {
        _billingClient.GetPlanByHandleAsync("eshop-pro", Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.ProPlan);
        _billingClient.EnsureCustomerAsync(SubscriptionBuilder.BuyerId, Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer(55, SubscriptionBuilder.BuyerId, SubscriptionBuilder.BuyerId, null, null));
        _billingClient.ListSubscriptionsForCustomerAsync(55, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Subscription>());
    }

    [Fact]
    public async Task EnrolsTheCustomerAndPublishesTheActivationNotification()
    {
        var created = SubscriptionBuilder.WithState(SubscriptionState.Active);
        _billingClient.CreateSubscriptionAsync(55, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(created);

        var subscription = await Service.SubscribeAsync(SubscriptionBuilder.BuyerId, "eshop-pro");

        Assert.Equal(101, subscription.Id);
        await _publisher.Received(1).Publish(
            Arg.Is<SubscriptionActivated>(n => n.Subscription.Id == 101), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsTheExistingActiveSubscriptionRatherThanCreatingASecondOne()
    {
        var existing = SubscriptionBuilder.WithState(SubscriptionState.Active, id: 900);
        _billingClient.ListSubscriptionsForCustomerAsync(55, Arg.Any<CancellationToken>())
            .Returns(new[] { existing });

        var subscription = await Service.SubscribeAsync(SubscriptionBuilder.BuyerId, "eshop-pro");

        Assert.Equal(900, subscription.Id);
        // A double-click must never produce a second enrolment.
        await _billingClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await _publisher.DidNotReceive().Publish(Arg.Any<SubscriptionActivated>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StillEnrolsWhenTheCustomerOnlyHasACancelledSubscription()
    {
        _billingClient.ListSubscriptionsForCustomerAsync(55, Arg.Any<CancellationToken>())
            .Returns(new[] { SubscriptionBuilder.WithState(SubscriptionState.Canceled, id: 900) });
        _billingClient.CreateSubscriptionAsync(55, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.WithState(SubscriptionState.Active));

        var subscription = await Service.SubscribeAsync(SubscriptionBuilder.BuyerId, "eshop-pro");

        Assert.Equal(101, subscription.Id);
        await _billingClient.Received(1).CreateSubscriptionAsync(55, "eshop-pro", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusesToEnrolAgainstAPlanHandleThatDoesNotResolve()
    {
        _billingClient.GetPlanByHandleAsync("stale-handle", Arg.Any<CancellationToken>())
            .Returns((SubscriptionPlan?)null);

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => Service.SubscribeAsync(SubscriptionBuilder.BuyerId, "stale-handle"));

        // Never guess a plan: no customer is touched and no subscription is created.
        await _billingClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsAnEmptyBuyerIdBeforeCallingTheProvider()
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Service.SubscribeAsync("", "eshop-pro"));

        await _billingClient.DidNotReceive().GetPlanByHandleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LetsTheProviderFailureSurfaceWhenEnrolmentItselfFails()
    {
        _billingClient.CreateSubscriptionAsync(55, "eshop-pro", Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingProviderException("plan requires a payment method", 422,
                new[] { "Credit card is required" }));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => Service.SubscribeAsync(SubscriptionBuilder.BuyerId, "eshop-pro"));

        Assert.Contains("Credit card is required", exception.ProviderErrors);
    }

    [Fact]
    public async Task KeepsTheSubscriptionWhenAnInProcessHandlerFails()
    {
        _billingClient.CreateSubscriptionAsync(55, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.WithState(SubscriptionState.Active));
        _publisher.Publish(Arg.Any<SubscriptionActivated>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("handler blew up"));

        // Eventing is best-effort: a failing handler must not roll back the enrolment.
        var subscription = await Service.SubscribeAsync(SubscriptionBuilder.BuyerId, "eshop-pro");

        Assert.Equal(101, subscription.Id);
    }

    [Fact]
    public async Task IdentifiesTheCustomerByTheEShopUserReferenceSoRepeatCallsAreIdempotent()
    {
        _billingClient.CreateSubscriptionAsync(55, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.WithState(SubscriptionState.Active));

        await Service.SubscribeAsync(SubscriptionBuilder.BuyerId, "eshop-pro");

        await _billingClient.Received(1).EnsureCustomerAsync(SubscriptionBuilder.BuyerId,
            SubscriptionBuilder.BuyerId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }
}
