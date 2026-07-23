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

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Services;

/// <summary>UC1 — subscribe to a plan.</summary>
public class SubscriptionServiceSubscribeTests
{
    private readonly IBillingClient _billingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly SubscriptionService _service;

    public SubscriptionServiceSubscribeTests()
    {
        _service = new SubscriptionService(_billingClient, _publisher, Substitute.For<IAppLogger<SubscriptionService>>());
    }

    [Fact]
    public async Task EnrollsTheCustomerAndPublishesSubscriptionActivated()
    {
        var created = SubscriptionBuilder.Subscription();
        _billingClient.FindPlanAsync(SubscriptionBuilder.ProPlanHandle, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Plan());
        _billingClient.EnsureCustomerAsync(Arg.Any<CustomerRegistration>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Customer());
        _billingClient.ListSubscriptionsAsync(SubscriptionBuilder.UserReference, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Subscription>());
        _billingClient.CreateSubscriptionAsync(SubscriptionBuilder.UserReference, SubscriptionBuilder.ProPlanHandle, Arg.Any<CancellationToken>())
            .Returns(created);

        var result = await _service.SubscribeAsync(SubscriptionBuilder.UserReference, SubscriptionBuilder.ProPlanHandle);

        Assert.Same(created, result);
        Assert.Equal(299.00m, result.PlanPrice);
        await _publisher.Received(1).Publish(
            Arg.Is<SubscriptionActivated>(n => n.Subscription.Id == created.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreatesTheProviderCustomerFromTheUserReference()
    {
        _billingClient.FindPlanAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(SubscriptionBuilder.Plan());
        _billingClient.EnsureCustomerAsync(Arg.Any<CustomerRegistration>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Customer());
        _billingClient.ListSubscriptionsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Subscription>());
        _billingClient.CreateSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription());

        await _service.SubscribeAsync(SubscriptionBuilder.UserReference, SubscriptionBuilder.ProPlanHandle);

        await _billingClient.Received(1).EnsureCustomerAsync(
            Arg.Is<CustomerRegistration>(r =>
                r.Reference == SubscriptionBuilder.UserReference &&
                r.Email == SubscriptionBuilder.UserReference &&
                r.FirstName == "customer"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsTheExistingActiveSubscriptionInsteadOfEnrollingTwice()
    {
        var existing = SubscriptionBuilder.Subscription(id: 555);
        _billingClient.FindPlanAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(SubscriptionBuilder.Plan());
        _billingClient.EnsureCustomerAsync(Arg.Any<CustomerRegistration>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Customer());
        _billingClient.ListSubscriptionsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new[] { existing });

        var result = await _service.SubscribeAsync(SubscriptionBuilder.UserReference, SubscriptionBuilder.ProPlanHandle);

        Assert.Equal(555, result.Id);
        await _billingClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _publisher.DidNotReceive().Publish(Arg.Any<SubscriptionActivated>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusesToEnrollASecondTimeOnADifferentPlanInsteadOfReportingAFalseSuccess()
    {
        var onBasic = SubscriptionBuilder.Subscription(id: 555, planHandle: SubscriptionBuilder.BasicPlanHandle, planPriceInCents: 2_900);
        _billingClient.FindPlanAsync(SubscriptionBuilder.ProPlanHandle, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Plan());
        _billingClient.EnsureCustomerAsync(Arg.Any<CustomerRegistration>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Customer());
        _billingClient.ListSubscriptionsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new[] { onBasic });

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.SubscribeAsync(SubscriptionBuilder.UserReference, SubscriptionBuilder.ProPlanHandle));

        Assert.Contains(SubscriptionBuilder.BasicPlanHandle, exception.Message);
        Assert.Contains(SubscriptionBuilder.ProPlanHandle, exception.Message);
        await _billingClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _publisher.DidNotReceive().Publish(Arg.Any<SubscriptionActivated>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnrollsAgainWhenTheOnlyExistingSubscriptionIsCancelled()
    {
        var cancelled = SubscriptionBuilder.Subscription(id: 555, state: SubscriptionState.Canceled);
        _billingClient.FindPlanAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(SubscriptionBuilder.Plan());
        _billingClient.EnsureCustomerAsync(Arg.Any<CustomerRegistration>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Customer());
        _billingClient.ListSubscriptionsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new[] { cancelled });
        _billingClient.CreateSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription(id: 777));

        var result = await _service.SubscribeAsync(SubscriptionBuilder.UserReference, SubscriptionBuilder.ProPlanHandle);

        Assert.Equal(777, result.Id);
    }

    [Fact]
    public async Task RefusesToEnrollAgainstAnUnresolvablePlan()
    {
        _billingClient.FindPlanAsync("ghost-plan", Arg.Any<CancellationToken>()).Returns((SubscriptionPlan?)null);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => _service.SubscribeAsync(SubscriptionBuilder.UserReference, "ghost-plan"));

        Assert.Contains("ghost-plan", exception.Message);
        await _billingClient.DidNotReceive().EnsureCustomerAsync(Arg.Any<CustomerRegistration>(), Arg.Any<CancellationToken>());
        await _billingClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SurfacesAProviderFailureFromEnrollmentAsATypedException()
    {
        _billingClient.FindPlanAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(SubscriptionBuilder.Plan());
        _billingClient.EnsureCustomerAsync(Arg.Any<CustomerRegistration>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Customer());
        _billingClient.ListSubscriptionsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Subscription>());
        _billingClient.CreateSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new BillingProviderException("Payment method required", 422));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => _service.SubscribeAsync(SubscriptionBuilder.UserReference, SubscriptionBuilder.ProPlanHandle));

        Assert.Equal(422, exception.StatusCode);
        await _publisher.DidNotReceive().Publish(Arg.Any<SubscriptionActivated>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task KeepsTheSubscriptionWhenTheInProcessNotificationFails()
    {
        var created = SubscriptionBuilder.Subscription();
        _billingClient.FindPlanAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(SubscriptionBuilder.Plan());
        _billingClient.EnsureCustomerAsync(Arg.Any<CustomerRegistration>(), Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Customer());
        _billingClient.ListSubscriptionsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Subscription>());
        _billingClient.CreateSubscriptionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(created);
        _publisher.Publish(Arg.Any<SubscriptionActivated>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("handler exploded"));

        // §2.5 — eventing is best-effort; a failing handler must not undo a successful enrollment.
        var result = await _service.SubscribeAsync(SubscriptionBuilder.UserReference, SubscriptionBuilder.ProPlanHandle);

        Assert.Same(created, result);
    }

    [Fact]
    public async Task ListsPlansStraightFromTheBillingClient()
    {
        var plans = new[] { SubscriptionBuilder.Plan(), SubscriptionBuilder.Plan(SubscriptionBuilder.BasicPlanHandle, 2_900) };
        _billingClient.ListPlansAsync(Arg.Any<CancellationToken>()).Returns(plans);

        var result = await _service.ListPlansAsync();

        Assert.Collection(result,
            plan => Assert.Equal(299.00m, plan.Price),
            plan => Assert.Equal(29.00m, plan.Price));
    }

    [Fact]
    public async Task ReturnsAnEmptyListForAUserWithNoSubscriptions()
    {
        _billingClient.ListSubscriptionsAsync("nobody@microsoft.com", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Subscription>());

        var result = await _service.ListSubscriptionsAsync("nobody@microsoft.com");

        Assert.Empty(result);
    }
}
