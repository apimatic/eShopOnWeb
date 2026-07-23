using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// The customer's subscription management surface: current state (UC1), period-to-date metered
/// usage (UC2), plan change with a proration preview (UC3), and the lifecycle actions (UC4).
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

    public IReadOnlyList<BillingSubscription> Subscriptions { get; private set; } = Array.Empty<BillingSubscription>();

    public BillingSubscription? ActiveSubscription { get; private set; }

    public IReadOnlyList<BillingPlan> AvailablePlans { get; private set; } = Array.Empty<BillingPlan>();

    /// <summary>Running metered usage for the current billing period; null when unavailable.</summary>
    public int? PeriodToDateUnits { get; private set; }

    /// <summary>Populated only while a plan change is awaiting the customer's confirmation.</summary>
    public PlanChangePreview? Preview { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(string? subscribed, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(subscribed))
        {
            StatusMessage = $"You are now subscribed to {subscribed}.";
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostRecordUsageAsync(
        int subscriptionId,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(async () =>
        {
            var result = await _subscriptionService.RecordUsageAsync(subscriptionId, quantity, memo, cancellationToken);

            StatusMessage = result.PeriodToDateUnavailable
                ? $"Recorded {result.Quantity} unit(s). The running total is temporarily unavailable, but the usage will appear on your next renewal invoice."
                : $"Recorded {result.Quantity} unit(s). Period-to-date usage is now {result.PeriodToDateUnits} unit(s), billed on your next renewal invoice.";
        });

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPreviewPlanChangeAsync(
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken)
    {
        try
        {
            Preview = await _subscriptionService.PreviewPlanChangeAsync(
                subscriptionId, targetPlanHandle, timing, cancellationToken);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            _logger.LogWarning("Plan change preview failed: {Message}", ex.Message);
            ErrorMessage = ex.Message;
            await LoadAsync(cancellationToken);
            return Page();
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostConfirmPlanChangeAsync(
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        string previewFingerprint,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(async () =>
        {
            var updated = await _subscriptionService.ChangePlanAsync(
                subscriptionId, targetPlanHandle, timing, previewFingerprint, cancellationToken);

            StatusMessage = timing == PlanChangeTiming.Immediate
                ? $"Your plan is now {updated.PlanName ?? updated.PlanHandle}."
                : $"Your plan will change to {targetPlanHandle} at your next renewal.";
        });

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostLifecycleAsync(
        int subscriptionId,
        SubscriptionLifecycleAction action,
        CancellationTiming cancellationTiming,
        string? reason,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(async () =>
        {
            var updated = await _subscriptionService.ApplyLifecycleActionAsync(
                subscriptionId, action, cancellationTiming, reason, cancellationToken);

            var effective = action == SubscriptionLifecycleAction.Cancel && cancellationTiming == CancellationTiming.EndOfPeriod
                ? $" It stays active until {updated.DelayedCancelAt ?? updated.CurrentPeriodEndsAt:d}."
                : string.Empty;

            StatusMessage = $"Your subscription is now {updated.State}.{effective}";
        });

        return RedirectToPage();
    }

    /// <summary>
    /// Runs a management action, turning the integration's typed failures into a message the
    /// customer can act on instead of an unhandled exception.
    /// </summary>
    private async Task ExecuteAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            _logger.LogWarning("Subscription action failed: {Message}", ex.Message);
            ErrorMessage = ex.Message;
        }
    }

    private static bool IsExpected(Exception ex) =>
        ex is BillingProviderException
           or BillingConfigurationException
           or InvalidSubscriptionOperationException
           or StalePlanChangePreviewException;

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var userReference = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userReference))
        {
            return;
        }

        try
        {
            Subscriptions = await _subscriptionService.ListSubscriptionsAsync(userReference, cancellationToken);
            ActiveSubscription = Subscriptions.FirstOrDefault(s => s.IsActive);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            _logger.LogWarning("Subscriptions could not be listed for {User}: {Message}", userReference, ex.Message);
            ErrorMessage ??= "Your subscriptions are temporarily unavailable. Please try again shortly.";
            return;
        }

        try
        {
            AvailablePlans = await _subscriptionService.ListPlansAsync(cancellationToken);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            // The current subscription still renders without the list of change targets.
            _logger.LogWarning("Plans could not be listed: {Message}", ex.Message);
        }

        if (ActiveSubscription is null)
        {
            return;
        }

        try
        {
            PeriodToDateUnits = await _subscriptionService.GetPeriodToDateUsageAsync(
                ActiveSubscription.Id, cancellationToken);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            // Usage is a panel on the page, not the page itself.
            _logger.LogWarning("Period-to-date usage could not be read: {Message}", ex.Message);
        }
    }
}
