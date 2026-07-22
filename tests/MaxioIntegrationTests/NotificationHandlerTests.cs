using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The in-process eventing acceptance bar: after a successful billing change the notification is
/// published and the registered handlers actually run. Nothing durable is promised beyond that.
/// </summary>
public class NotificationHandlerTests
{
    [Fact]
    public async Task An_activation_is_audited_with_the_plan_and_the_price()
    {
        var logger = new CapturingAppLogger<SubscriptionAuditHandler>();
        var handler = new SubscriptionAuditHandler(logger);

        await handler.Handle(
            new SubscriptionActivated(1001, "demouser@microsoft.com", "eshop-pro", "Pro Plan", 299.00m,
                DateTimeOffset.Parse("2026-08-22T00:00:00Z")),
            CancellationToken.None);

        var entry = Assert.Single(logger.Information);
        Assert.Contains("1001", entry, StringComparison.Ordinal);
        Assert.Contains("eshop-pro", entry, StringComparison.Ordinal);
        Assert.Contains("299.00", entry, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_plan_change_is_audited_with_both_plans_and_the_proration()
    {
        var logger = new CapturingAppLogger<SubscriptionAuditHandler>();
        var handler = new SubscriptionAuditHandler(logger);

        await handler.Handle(
            new SubscriptionPlanChanged(1001, "demouser@microsoft.com", "eshop-pro", "basic-plan", -134.50m,
                PlanChangeTiming.Immediately, null),
            CancellationToken.None);

        var entry = Assert.Single(logger.Information);
        Assert.Contains("eshop-pro", entry, StringComparison.Ordinal);
        Assert.Contains("basic-plan", entry, StringComparison.Ordinal);
        Assert.Contains("134.50", entry, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_state_change_is_audited_with_the_old_and_new_state()
    {
        var logger = new CapturingAppLogger<SubscriptionAuditHandler>();
        var handler = new SubscriptionAuditHandler(logger);

        await handler.Handle(
            new SubscriptionStateChanged(1001, "demouser@microsoft.com", SubscriptionLifecycleAction.Pause,
                SubscriptionLifecycleState.Active, SubscriptionLifecycleState.Paused, null),
            CancellationToken.None);

        var entry = Assert.Single(logger.Information);
        Assert.Contains("Active", entry, StringComparison.Ordinal);
        Assert.Contains("Paused", entry, StringComparison.Ordinal);
        Assert.Contains("Pause", entry, StringComparison.Ordinal);
    }

    private sealed class CapturingAppLogger<T> : IAppLogger<T>
    {
        public List<string> Information { get; } = new();

        public void LogInformation(string message, params object[] args)
            => Information.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, message, args));

        public void LogWarning(string message, params object[] args)
        {
        }
    }
}
