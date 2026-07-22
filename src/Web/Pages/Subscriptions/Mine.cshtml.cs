using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// The customer's subscription management surface: view state and next billing date (UC1),
/// see and report metered usage (UC2), change plan with a proration preview (UC3), and run the
/// lifecycle actions (UC4).
/// </summary>
[Authorize]
public class MineModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly IAppLogger<MineModel> _logger;

    public MineModel(ISubscriptionService subscriptionService, IAppLogger<MineModel> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    public IReadOnlyCollection<Subscription> Subscriptions { get; private set; } = Array.Empty<Subscription>();

    public IReadOnlyCollection<SubscriptionPlan> Plans { get; private set; } = Array.Empty<SubscriptionPlan>();

    /// <summary>Period-to-date usage per subscription, keyed by subscription id (UC2).</summary>
    public Dictionary<int, UsageSummary> Usage { get; } = new();

    /// <summary>The quote shown before a plan change is confirmed (UC3 step 2).</summary>
    public PlanChangePreview? Preview { get; private set; }

    public string? Message { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task OnGet(int? subscribed)
    {
        if (subscribed is not null)
        {
            Message = $"You are subscribed. Subscription {subscribed} is now active.";
        }

        await LoadAsync();
    }

    public async Task<IActionResult> OnPostUsage(int subscriptionId, decimal quantity, string? memo)
    {
        return await RunAsync(async () =>
        {
            var summary = await _subscriptionService.RecordUsageAsync(subscriptionId, quantity, memo);
            Message = summary.TotalAvailable
                ? $"Recorded {quantity:0.##} unit(s). Period-to-date: {summary.PeriodToDateQuantity:0.##} " +
                  $"($ {summary.EstimatedCharge:N2} on your next invoice)."
                : $"Recorded {quantity:0.##} unit(s). The running total is temporarily unavailable.";
        });
    }

    public async Task<IActionResult> OnPostPreview(int subscriptionId, string targetPlanHandle, string timing)
    {
        return await RunAsync(async () =>
        {
            Preview = await _subscriptionService.PreviewPlanChangeAsync(
                subscriptionId, targetPlanHandle, ParseTiming(timing));
        });
    }

    public async Task<IActionResult> OnPostChangePlan(int subscriptionId,
        string targetPlanHandle,
        string timing,
        long proratedAdjustmentInCents,
        long chargeInCents,
        long paymentDueInCents,
        long creditAppliedInCents,
        string currentPlanHandle)
    {
        return await RunAsync(async () =>
        {
            var planChangeTiming = ParseTiming(timing);

            // Echo back exactly what was quoted, so the change is refused if it has moved.
            var confirmed = new PlanChangePreview(subscriptionId, currentPlanHandle, targetPlanHandle, planChangeTiming,
                proratedAdjustmentInCents, chargeInCents, paymentDueInCents, creditAppliedInCents);

            var subscription = await _subscriptionService.ChangePlanAsync(
                subscriptionId, targetPlanHandle, planChangeTiming, confirmed);

            Message = planChangeTiming == PlanChangeTiming.AtNextRenewal
                ? $"{currentPlanHandle} will change to {targetPlanHandle} at your next renewal on {subscription.CurrentPeriodEndsAt:d}."
                : $"Moved from {currentPlanHandle} to {subscription.PlanHandle}. You paid $ {confirmed.PaymentDue:N2}.";
        });
    }

    public async Task<IActionResult> OnPostLifecycle(int subscriptionId, string action, string? timing, string? reason)
    {
        return await RunAsync(async () =>
        {
            var subscription = action switch
            {
                SubscriptionActions.Pause => await _subscriptionService.PauseAsync(subscriptionId),
                SubscriptionActions.Resume => await _subscriptionService.ResumeAsync(subscriptionId),
                SubscriptionActions.Cancel => await _subscriptionService.CancelAsync(
                    subscriptionId,
                    string.Equals(timing, nameof(CancellationTiming.EndOfPeriod), StringComparison.OrdinalIgnoreCase)
                        ? CancellationTiming.EndOfPeriod
                        : CancellationTiming.Immediate,
                    reason),
                SubscriptionActions.Reactivate => await _subscriptionService.ReactivateAsync(subscriptionId),
                _ => throw new ArgumentException($"'{action}' is not a lifecycle action.", nameof(action))
            };

            Message = subscription.CancelAtEndOfPeriod
                ? $"Subscription {subscription.Id} is {subscription.State} and will cancel on {subscription.CurrentPeriodEndsAt:d}."
                : $"Subscription {subscription.Id} is now {subscription.State}.";
        });
    }

    /// <summary>Runs an action, turning the domain's typed failures into a message on the page.</summary>
    private async Task<IActionResult> RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is IllegalSubscriptionTransitionException
            or PlanChangeNotApplicableException
            or StalePlanChangePreviewException
            or SubscriptionNotFoundException
            or PlanNotFoundException
            or BillingConfigurationException
            or BillingProviderException
            or ArgumentException)
        {
            _logger.LogWarning(ex.Message);
            ErrorMessage = ex.Message;
        }

        await LoadAsync();
        return Page();
    }

    private static PlanChangeTiming ParseTiming(string? timing) =>
        Enum.TryParse<PlanChangeTiming>(timing, ignoreCase: true, out var parsed)
            ? parsed
            : PlanChangeTiming.Immediately;

    private async Task LoadAsync()
    {
        if (TempData["SubscriptionMessage"] is string carried)
        {
            ErrorMessage ??= carried;
        }

        var userName = User.Identity?.Name;
        if (string.IsNullOrEmpty(userName))
        {
            return;
        }

        try
        {
            Subscriptions = await _subscriptionService.GetSubscriptionsForUserAsync(userName);
            Plans = await _subscriptionService.ListPlansAsync();
        }
        catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException)
        {
            _logger.LogWarning("Loading subscriptions for {0} failed: {1}", userName, ex.Message);
            ErrorMessage ??= "Your subscriptions are unavailable right now. Please try again shortly.";
            return;
        }

        foreach (var subscription in Subscriptions.Where(s => s.IsLive))
        {
            try
            {
                Usage[subscription.Id] = await _subscriptionService.GetUsageAsync(subscription.Id);
            }
            catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException)
            {
                // A usage read-back failure must not hide the subscriptions themselves (UC2).
                _logger.LogWarning("Reading usage for subscription {0} failed: {1}", subscription.Id, ex.Message);
            }
        }
    }
}
