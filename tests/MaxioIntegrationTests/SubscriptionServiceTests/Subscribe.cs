using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.SubscriptionServiceTests;

public class Subscribe
{
    private const string BuyerId = "demouser@microsoft.com";

    private readonly IBillingClient _billingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly SubscriptionService _service;

    public Subscribe()
    {
        _service = new SubscriptionService(_billingClient, _publisher, new NullAppLogger<SubscriptionService>());
    }

    [Fact]
    public async Task EnrollsTheCustomerAndReturnsTheSubscription()
    {
        ArrangePlan();
        ArrangeCustomer();
        _billingClient.ListSubscriptionsForCustomerAsync(TestData.CustomerId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<BillingSubscription>());
        _billingClient.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Subscription());

        var subscription = await _service.SubscribeAsync(BuyerId, "eshop-pro");

        Assert.Equal(BuyerId, subscription.BuyerId);
        Assert.Equal(TestData.SubscriptionId, subscription.ProviderSubscriptionId);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.True(subscription.IsActive);
    }

    [Fact]
    public async Task EnsuresTheProviderSideCustomerExistsBeforeEnrolling()
    {
        ArrangePlan();
        ArrangeCustomer();
        _billingClient.ListSubscriptionsForCustomerAsync(TestData.CustomerId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<BillingSubscription>());
        _billingClient.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Subscription());

        await _service.SubscribeAsync(BuyerId, "eshop-pro");

        // The eShopOnWeb username is the stable provider-side reference, which is what makes a
        // repeated subscribe idempotent.
        await _billingClient.Received(1).EnsureCustomerAsync(
            Arg.Is<EnsureCustomerRequest>(r => r.Reference == BuyerId && r.Email == BuyerId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishesSubscriptionActivatedCarryingThePlanAndNextBillingDate()
    {
        ArrangePlan();
        ArrangeCustomer();
        _billingClient.ListSubscriptionsForCustomerAsync(TestData.CustomerId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<BillingSubscription>());
        _billingClient.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Subscription());

        await _service.SubscribeAsync(BuyerId, "eshop-pro");

        await _publisher.Received(1).Publish(
            Arg.Is<SubscriptionActivated>(n =>
                n.BuyerId == BuyerId &&
                n.SubscriptionId == TestData.SubscriptionId &&
                n.PlanHandle == "eshop-pro" &&
                n.PlanPriceInCents == 29900 &&
                n.NextBillingDate == TestData.NextAssessment),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A double-click must never produce a second enrollment: the existing active subscription is
    /// returned instead, and nothing is created or announced.
    /// </summary>
    [Fact]
    public async Task ReturnsTheExistingActiveSubscriptionInsteadOfEnrollingTwice()
    {
        ArrangePlan();
        ArrangeCustomer();
        _billingClient.ListSubscriptionsForCustomerAsync(TestData.CustomerId, Arg.Any<CancellationToken>())
            .Returns(new[] { TestData.Subscription() });

        var subscription = await _service.SubscribeAsync(BuyerId, "eshop-pro");

        Assert.Equal(TestData.SubscriptionId, subscription.ProviderSubscriptionId);
        await _billingClient.DidNotReceive().CreateSubscriptionAsync(
            Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>());
        await _publisher.DidNotReceive().Publish(Arg.Any<SubscriptionActivated>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A cancelled subscription does not block a fresh enrollment.</summary>
    [Fact]
    public async Task EnrollsAgainWhenThePreviousSubscriptionIsNoLongerActive()
    {
        ArrangePlan();
        ArrangeCustomer();
        _billingClient.ListSubscriptionsForCustomerAsync(TestData.CustomerId, Arg.Any<CancellationToken>())
            .Returns(new[] { TestData.Subscription(SubscriptionState.Canceled) });
        _billingClient.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Subscription());

        await _service.SubscribeAsync(BuyerId, "eshop-pro");

        await _billingClient.Received(1).CreateSubscriptionAsync(
            Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An unresolvable plan handle is a seeding problem. Enrolling against a guessed plan would
    /// bill the customer for something they did not choose.
    /// </summary>
    [Fact]
    public async Task RefusesToEnrollWhenThePlanHandleDoesNotResolve()
    {
        _billingClient.FindPlanByHandleAsync("ghost-plan", Arg.Any<CancellationToken>()).Returns((BillingPlan?)null);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(() =>
            _service.SubscribeAsync(BuyerId, "ghost-plan"));

        Assert.Contains("ghost-plan", exception.Message);
        await _billingClient.DidNotReceive().EnsureCustomerAsync(
            Arg.Any<EnsureCustomerRequest>(), Arg.Any<CancellationToken>());
        await _billingClient.DidNotReceive().CreateSubscriptionAsync(
            Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Eventing is best-effort: a failing in-process handler must not undo an enrollment the
    /// provider has already committed.
    /// </summary>
    [Fact]
    public async Task KeepsTheSubscriptionWhenTheNotificationHandlerFails()
    {
        ArrangePlan();
        ArrangeCustomer();
        _billingClient.ListSubscriptionsForCustomerAsync(TestData.CustomerId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<BillingSubscription>());
        _billingClient.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Subscription());
        _publisher.Publish(Arg.Any<SubscriptionActivated>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("the confirmation email handler blew up"));

        var subscription = await _service.SubscribeAsync(BuyerId, "eshop-pro");

        Assert.Equal(TestData.SubscriptionId, subscription.ProviderSubscriptionId);
    }

    [Fact]
    public async Task LetsAProviderFailureDuringEnrollmentReachTheCaller()
    {
        ArrangePlan();
        ArrangeCustomer();
        _billingClient.ListSubscriptionsForCustomerAsync(TestData.CustomerId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<BillingSubscription>());
        _billingClient.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingProviderException("No payment method was on file for the $299.00 balance"));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() =>
            _service.SubscribeAsync(BuyerId, "eshop-pro"));

        Assert.Contains("No payment method", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RejectsABlankBuyerBeforeTouchingTheProvider(string buyerId)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _service.SubscribeAsync(buyerId, "eshop-pro"));

        await _billingClient.DidNotReceiveWithAnyArgs().FindPlanByHandleAsync(default!, default);
    }

    private void ArrangePlan() =>
        _billingClient.FindPlanByHandleAsync("eshop-pro", Arg.Any<CancellationToken>()).Returns(TestData.ProPlan);

    private void ArrangeCustomer() =>
        _billingClient.EnsureCustomerAsync(Arg.Any<EnsureCustomerRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Customer);
}
