using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Seam;

/// <summary>
/// The in-process reactions to a lifecycle change. They run after the provider call has already
/// succeeded, so none of them may throw back into the caller.
/// </summary>
public class NotificationHandlerTests
{
    private const string UserReference = "shopper@example.com";

    private static BillingSubscription ASubscription(
        SubscriptionStatus status = SubscriptionStatus.Active) =>
        new BillingSubscription(93491347, status, "active")
        {
            PlanHandle = "eshop-pro",
            PlanName = "Pro Plan",
            PlanPrice = 299m,
            CurrentPeriodEndsAt = new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero)
        };

    // --- Audit log ---

    [Fact]
    public async Task ActivationIsAudited()
    {
        var logger = Substitute.For<IAppLogger<SubscriptionAuditLogHandler>>();
        var handler = new SubscriptionAuditLogHandler(logger);

        await handler.Handle(new SubscriptionActivated(UserReference, ASubscription()),
            CancellationToken.None);

        logger.ReceivedWithAnyArgs(1).LogInformation(default!);
    }

    [Fact]
    public async Task APlanChangeIsAudited()
    {
        var logger = Substitute.For<IAppLogger<SubscriptionAuditLogHandler>>();
        var handler = new SubscriptionAuditLogHandler(logger);

        await handler.Handle(
            new SubscriptionPlanChanged(UserReference, ASubscription(), "basic-plan",
                PlanChangeTiming.Immediate),
            CancellationToken.None);

        logger.ReceivedWithAnyArgs(1).LogInformation(default!);
    }

    [Fact]
    public async Task AStateChangeIsAudited()
    {
        var logger = Substitute.For<IAppLogger<SubscriptionAuditLogHandler>>();
        var handler = new SubscriptionAuditLogHandler(logger);

        await handler.Handle(
            new SubscriptionStateChanged(UserReference, ASubscription(SubscriptionStatus.OnHold),
                SubscriptionStatus.Active, "pause"),
            CancellationToken.None);

        logger.ReceivedWithAnyArgs(1).LogInformation(default!);
    }

    // --- Notification shape ---

    [Fact]
    public void AnImmediatePlanChangeReportsThePlanTheSubscriptionIsNowOn()
    {
        var notification = new SubscriptionPlanChanged(UserReference, ASubscription(), "basic-plan",
            PlanChangeTiming.Immediate);

        Assert.Equal("eshop-pro", notification.NewPlanHandle);
        Assert.Equal("basic-plan", notification.PreviousPlanHandle);
    }

    [Fact]
    public void ADeferredPlanChangeReportsThePlanTheSubscriptionWillMoveTo()
    {
        var scheduled = new BillingSubscription(1, SubscriptionStatus.Active, "active")
        {
            PlanHandle = "eshop-pro",
            NextPlanHandle = "basic-plan"
        };

        var notification = new SubscriptionPlanChanged(UserReference, scheduled, "eshop-pro",
            PlanChangeTiming.AtNextRenewal);

        // The subscription is still on the old plan, so the meaningful "new plan" is the scheduled one.
        Assert.Equal("basic-plan", notification.NewPlanHandle);
    }

    [Fact]
    public void AStateChangeCarriesBothTheOldAndNewState()
    {
        var notification = new SubscriptionStateChanged(UserReference,
            ASubscription(SubscriptionStatus.OnHold), SubscriptionStatus.Active, "pause");

        Assert.Equal(SubscriptionStatus.Active, notification.PreviousStatus);
        Assert.Equal(SubscriptionStatus.OnHold, notification.NewStatus);
    }

    // --- Confirmation email ---

    [Fact]
    public async Task ActivationSendsAConfirmationCarryingThePlanPriceAndNextBillingDate()
    {
        var emailSender = Substitute.For<IEmailSender>();
        var handler = new SendSubscriptionConfirmationHandler(emailSender,
            Substitute.For<IAppLogger<SendSubscriptionConfirmationHandler>>());

        await handler.Handle(new SubscriptionActivated(UserReference, ASubscription()),
            CancellationToken.None);

        await emailSender.Received(1).SendEmailAsync(UserReference,
            Arg.Any<string>(),
            Arg.Is<string>(body => body.Contains("Pro Plan") && body.Contains("299.00")));
    }

    [Fact]
    public async Task AFailingMailerNeverBreaksAnAlreadyActiveSubscription()
    {
        var emailSender = Substitute.For<IEmailSender>();
        emailSender.SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .ThrowsAsync(new InvalidOperationException("SMTP is down"));

        var logger = Substitute.For<IAppLogger<SendSubscriptionConfirmationHandler>>();
        var handler = new SendSubscriptionConfirmationHandler(emailSender, logger);

        await handler.Handle(new SubscriptionActivated(UserReference, ASubscription()),
            CancellationToken.None);

        logger.ReceivedWithAnyArgs(1).LogWarning(default!);
    }

    [Fact]
    public async Task AConfirmationIsStillSentWhenNoBillingDateIsScheduled()
    {
        var emailSender = Substitute.For<IEmailSender>();
        var handler = new SendSubscriptionConfirmationHandler(emailSender,
            Substitute.For<IAppLogger<SendSubscriptionConfirmationHandler>>());

        var withoutPeriod = new BillingSubscription(1, SubscriptionStatus.Active, "active")
        {
            PlanHandle = "eshop-pro",
            PlanPrice = 299m
        };

        await handler.Handle(new SubscriptionActivated(UserReference, withoutPeriod),
            CancellationToken.None);

        await emailSender.Received(1).SendEmailAsync(UserReference, Arg.Any<string>(),
            Arg.Is<string>(body => body.Contains("not scheduled")));
    }
}
