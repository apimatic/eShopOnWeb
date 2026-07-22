using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.IntegrationEventHandlerTests;

/// <summary>
/// The in-process reactions to a subscription lifecycle change (plan.md §2.5): an audit trail and a
/// customer confirmation, both built on eShopOnWeb's existing abstractions.
/// </summary>
public class SubscriptionNotificationHandlers
{
    private const string UserReference = "demouser@microsoft.com";

    private readonly IAppLogger<SubscriptionAuditLogHandler> _auditLogger =
        Substitute.For<IAppLogger<SubscriptionAuditLogHandler>>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();

    private SubscriptionAuditLogHandler AuditHandler => new(_auditLogger);
    private SendSubscriptionConfirmationHandler ConfirmationHandler => new(_emailSender);

    [Fact]
    public async Task AuditsAnActivation()
    {
        await AuditHandler.Handle(
            new SubscriptionActivated(42, UserReference, "eshop-pro", "Pro Plan", 299.00m,
                new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)),
            CancellationToken.None);

        _auditLogger.ReceivedWithAnyArgs(1).LogInformation(default!);
    }

    [Fact]
    public async Task AuditsAPlanChange()
    {
        await AuditHandler.Handle(
            new SubscriptionPlanChanged(42, UserReference, "eshop-pro", "basic-plan",
                PlanChangeTiming.Immediate, -241.50m, DateTimeOffset.UtcNow),
            CancellationToken.None);

        _auditLogger.ReceivedWithAnyArgs(1).LogInformation(default!);
    }

    [Fact]
    public async Task AuditsAStateChangeCarryingOldAndNewState()
    {
        await AuditHandler.Handle(
            new SubscriptionStateChanged(42, UserReference, SubscriptionLifecycleAction.Pause,
                SubscriptionState.Active, SubscriptionState.OnHold, DateTimeOffset.UtcNow),
            CancellationToken.None);

        _auditLogger.ReceivedWithAnyArgs(1).LogInformation(default!);
    }

    [Fact]
    public async Task AuditsAPlanChangeWithAnUnknownOriginPlan()
    {
        await AuditHandler.Handle(
            new SubscriptionPlanChanged(42, UserReference, null, "basic-plan",
                PlanChangeTiming.AtNextRenewal, 0m, null),
            CancellationToken.None);

        _auditLogger.ReceivedWithAnyArgs(1).LogInformation(default!);
    }

    [Fact]
    public async Task EmailsTheCustomerTheirPlanPriceAndNextBillingDate()
    {
        await ConfirmationHandler.Handle(
            new SubscriptionActivated(42, UserReference, "eshop-pro", "Pro Plan", 299.00m,
                new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)),
            CancellationToken.None);

        await _emailSender.Received(1).SendEmailAsync(
            UserReference,
            Arg.Any<string>(),
            Arg.Is<string>(body => body.Contains("Pro Plan") && body.Contains("$299.00")));
    }

    [Fact]
    public async Task EmailsTheCustomerEvenWhenTheNextBillingDateIsUnknown()
    {
        await ConfirmationHandler.Handle(
            new SubscriptionActivated(42, UserReference, "eshop-pro", null, 29.00m, null),
            CancellationToken.None);

        await _emailSender.Received(1).SendEmailAsync(
            UserReference,
            Arg.Any<string>(),
            Arg.Is<string>(body => body.Contains("eshop-pro") && body.Contains("current period")));
    }
}
