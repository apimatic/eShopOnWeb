using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The in-process reactions to subscription events, and — most importantly — the guarantee that a
/// billing failure can never escape into eShopOnWeb's own checkout path.
/// </summary>
public class SubscriptionNotificationHandlers
{
    private const string User = SubscriptionServiceHarness.UserName;

    [Fact]
    public async Task ActivationSendsTheCustomerAConfirmationNamingThePlanAndPrice()
    {
        var emailSender = Substitute.For<IEmailSender>();
        var handler = new SubscriptionActivatedHandler(
            emailSender, Substitute.For<IAppLogger<SubscriptionActivatedHandler>>());

        await handler.Handle(
            new SubscriptionActivated(User, SubscriptionServiceHarness.Sub()), CancellationToken.None);

        await emailSender.Received(1).SendEmailAsync(
            User,
            Arg.Is<string>(s => s.Contains("subscription", StringComparison.OrdinalIgnoreCase)),
            Arg.Is<string>(b => b.Contains("Pro Plan", StringComparison.Ordinal)
                                && b.Contains("299", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task PlanChangeIsRecordedForAudit()
    {
        var logger = Substitute.For<IAppLogger<SubscriptionPlanChangedHandler>>();
        var handler = new SubscriptionPlanChangedHandler(logger);

        await handler.Handle(
            new SubscriptionPlanChanged(
                SubscriptionServiceHarness.Sub(), "eshop-pro", "basic-plan", PlanChangeTiming.Immediate, 50.00m),
            CancellationToken.None);

        logger.ReceivedWithAnyArgs(1).LogInformation(default!);
    }

    [Fact]
    public async Task StateChangeIsRecordedForAudit()
    {
        var logger = Substitute.For<IAppLogger<SubscriptionStateChangedHandler>>();
        var handler = new SubscriptionStateChangedHandler(logger);

        await handler.Handle(
            new SubscriptionStateChanged(
                SubscriptionServiceHarness.Sub(),
                SubscriptionState.Active,
                SubscriptionState.Paused,
                SubscriptionLifecycleAction.Pause,
                reason: null),
            CancellationToken.None);

        logger.ReceivedWithAnyArgs(1).LogInformation(default!);
    }

    [Fact]
    public async Task APlacedOrderMetersOneBillableUnit()
    {
        var subscriptionService = Substitute.For<ISubscriptionService>();
        subscriptionService
            .RecordUsageForUserAsync(User, 1m, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new UsageReceipt { Recorded = SubscriptionServiceHarness.Usage(), PeriodToDateUnits = 4 });

        var handler = new OrderPlacedUsageHandler(
            subscriptionService, Substitute.For<IAppLogger<OrderPlacedUsageHandler>>());

        await handler.Handle(new OrderPlaced(42, User), CancellationToken.None);

        await subscriptionService.Received(1).RecordUsageForUserAsync(
            User,
            1m,
            Arg.Is<string?>(m => m != null && m.Contains("42", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnOrderFromABuyerWithNoSubscriptionMetersNothingAndDoesNotFail()
    {
        var subscriptionService = Substitute.For<ISubscriptionService>();
        subscriptionService
            .RecordUsageForUserAsync(User, 1m, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((UsageReceipt?)null);

        var handler = new OrderPlacedUsageHandler(
            subscriptionService, Substitute.For<IAppLogger<OrderPlacedUsageHandler>>());

        await handler.Handle(new OrderPlaced(42, User), CancellationToken.None);
    }

    [Theory]
    [MemberData(nameof(BillingFailures))]
    public async Task ABillingFailureNeverEscapesIntoTheCheckoutPath(Exception failure)
    {
        var subscriptionService = Substitute.For<ISubscriptionService>();
        subscriptionService
            .RecordUsageForUserAsync(User, 1m, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(failure);

        var logger = Substitute.For<IAppLogger<OrderPlacedUsageHandler>>();
        var handler = new OrderPlacedUsageHandler(subscriptionService, logger);

        // The order has already been persisted by the time this runs. Whatever the billing
        // provider does, this must complete quietly — an exception here would surface as a failed
        // checkout for an order that actually succeeded.
        await handler.Handle(new OrderPlaced(42, User), CancellationToken.None);

        logger.ReceivedWithAnyArgs(1).LogWarning(default!);
    }

    public static TheoryData<Exception> BillingFailures() => new()
    {
        new BillingProviderUnavailableException("RecordUsage", "provider down"),
        new BillingConfigurationException("component is not metered"),
        new InvalidSubscriptionOperationException("quantity must be positive"),
        new BillingProviderValidationException("RecordUsage", "rejected"),
        new InvalidOperationException("something entirely unexpected")
    };
}
