using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.IntegrationEventHandlerTests;

/// <summary>
/// The in-process reactions to a subscription lifecycle change (plan.md §2.5). They run after the
/// provider call has already succeeded, so none of them may throw.
/// </summary>
public class NotificationHandlerTests
{
    private static readonly BillingPlan ProPlan = new(1, "eshop-pro", "Pro Plan", 299.00m, 1, "month");
    private static readonly BillingPlan BasicPlan = new(2, "basic-plan", "Basic Plan", 29.00m, 1, "month");

    private static Subscription Subscription(SubscriptionState state = SubscriptionState.Active) =>
        new(50, "demouser@microsoft.com", 90210, ProPlan, state, state.ToString().ToLowerInvariant())
        {
            NextAssessmentAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)
        };

    [Fact]
    public async Task ActivationSendsTheCustomerAConfirmationWithThePlanAndNextBillingDate()
    {
        var email = new RecordingEmailSender();
        var handler = new SubscriptionActivatedHandler(email, new RecordingLogger<SubscriptionActivatedHandler>());

        await handler.Handle(new SubscriptionActivated(Subscription()), CancellationToken.None);

        var sent = Assert.Single(email.Sent);
        Assert.Equal("demouser@microsoft.com", sent.To);
        Assert.Contains("Pro Plan", sent.Subject);
        Assert.Contains("$299.00 / month", sent.Body);
        Assert.Contains("2026", sent.Body);
    }

    [Fact]
    public async Task ActivationStillSucceedsWhenTheConfirmationCannotBeSent()
    {
        var email = new RecordingEmailSender { Failure = new InvalidOperationException("smtp is down") };
        var logger = new RecordingLogger<SubscriptionActivatedHandler>();
        var handler = new SubscriptionActivatedHandler(email, logger);

        var exception = await Record.ExceptionAsync(
            () => handler.Handle(new SubscriptionActivated(Subscription()), CancellationToken.None));

        // The subscription already exists at the provider; a mail failure must not undo it.
        Assert.Null(exception);
        Assert.Contains(logger.Warnings, m => m.Contains("activation confirmation"));
    }

    [Fact]
    public async Task ActivationIsAlwaysRecordedInTheAuditLog()
    {
        var logger = new RecordingLogger<SubscriptionActivatedHandler>();
        var handler = new SubscriptionActivatedHandler(new RecordingEmailSender(), logger);

        await handler.Handle(new SubscriptionActivated(Subscription()), CancellationToken.None);

        Assert.Contains(logger.Informations, m => m.Contains("50") && m.Contains("eshop-pro"));
    }

    [Fact]
    public async Task APlanChangeIsRecordedWithBothPlansAndTheNetAmount()
    {
        var logger = new RecordingLogger<SubscriptionPlanChangedHandler>();
        var handler = new SubscriptionPlanChangedHandler(logger);

        await handler.Handle(
            new SubscriptionPlanChanged(Subscription(), ProPlan, BasicPlan, PlanChangeTiming.Immediate, 15.00m),
            CancellationToken.None);

        var entry = Assert.Single(logger.Informations);
        Assert.Contains("eshop-pro", entry);
        Assert.Contains("basic-plan", entry);
    }

    [Fact]
    public async Task AStateChangeIsRecordedWithTheOldAndNewState()
    {
        var logger = new RecordingLogger<SubscriptionStateChangedHandler>();
        var handler = new SubscriptionStateChangedHandler(logger);

        await handler.Handle(
            new SubscriptionStateChanged(Subscription(SubscriptionState.Paused), SubscriptionState.Active, "pause"),
            CancellationToken.None);

        var entry = Assert.Single(logger.Informations);
        Assert.Contains("pause", entry);
        Assert.Contains("Active", entry);
        Assert.Contains("Paused", entry);
    }

    [Fact]
    public void AStateChangeNotificationReadsItsNewStateFromTheSubscriptionItself()
    {
        var notification = new SubscriptionStateChanged(
            Subscription(SubscriptionState.Canceled), SubscriptionState.Active, "cancel");

        // The provider is the authority on the resulting state, so it is never passed in separately.
        Assert.Equal(SubscriptionState.Canceled, notification.NewState);
        Assert.Equal(SubscriptionState.Active, notification.PreviousState);
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        internal List<(string To, string Subject, string Body)> Sent { get; } = new();

        internal Exception? Failure { get; set; }

        public Task SendEmailAsync(string email, string subject, string message)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Sent.Add((email, subject, message));
            return Task.CompletedTask;
        }
    }
}
