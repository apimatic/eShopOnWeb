using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// The customer's subscription management surface: current state and next billing date (UC1),
/// the pay-as-you-go usage panel (UC2), plan change with a proration preview (UC3), and the
/// lifecycle actions (UC4).
/// </summary>
public class MineModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly ISubscriptionCatalogSettings _catalogSettings;

    public MineModel(ISubscriptionService subscriptionService, ISubscriptionCatalogSettings catalogSettings)
    {
        _subscriptionService = subscriptionService;
        _catalogSettings = catalogSettings;
    }

    public IReadOnlyList<Subscription> Subscriptions { get; private set; } = Array.Empty<Subscription>();

    public IReadOnlyList<SubscriptionPlan> Plans { get; private set; } = Array.Empty<SubscriptionPlan>();

    /// <summary>Period-to-date metered units, keyed by subscription id, for subscriptions that have any.</summary>
    public IReadOnlyDictionary<int, int?> UsageBySubscription { get; private set; } = new Dictionary<int, int?>();

    /// <summary>Set when the customer asked for a plan-change quote and must now confirm it.</summary>
    public PlanChangePreview? PendingPreview { get; private set; }

    public string? ErrorMessage { get; private set; }

    public string? StatusMessage { get; private set; }

    /// <summary>The subscription to draw attention to, for example straight after subscribing.</summary>
    [BindProperty(SupportsGet = true)]
    public int? Highlight { get; set; }

    public string MeteredComponentHandle => _catalogSettings.MeteredComponentHandle;

    public async Task OnGet(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostRecordUsage(int subscriptionId, decimal quantity, string? memo, CancellationToken cancellationToken)
    {
        try
        {
            var receipt = await _subscriptionService.RecordUsageAsync(subscriptionId, quantity, memo, cancellationToken);

            StatusMessage = receipt.PeriodToDateAvailable
                ? $"Recorded {receipt.Recorded.Quantity:N0} unit(s). Period-to-date total is now " +
                  $"{receipt.PeriodToDateUnits:N0} unit(s); this is billed on your next renewal invoice."
                : $"Recorded {receipt.Recorded.Quantity:N0} unit(s). The running total is temporarily " +
                  "unavailable, but the usage is billed on your next renewal invoice.";
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            ErrorMessage = ex.Message;
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostPreviewPlanChange(
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken)
    {
        try
        {
            PendingPreview = await _subscriptionService.PreviewPlanChangeAsync(
                subscriptionId, targetPlanHandle, timing, cancellationToken);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            ErrorMessage = ex.Message;
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostConfirmPlanChange(
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        string previewToken,
        CancellationToken cancellationToken)
    {
        try
        {
            // The token pins the quote the customer actually saw; a moved price is refused rather
            // than silently charged.
            var subscription = await _subscriptionService.ChangePlanAsync(
                subscriptionId, targetPlanHandle, timing, previewToken, cancellationToken);

            StatusMessage = timing == PlanChangeTiming.Immediate
                ? $"Subscription {subscription.Id} is now on {subscription.PlanName ?? targetPlanHandle}."
                : $"Subscription {subscription.Id} will move to {targetPlanHandle} at the next renewal.";
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            ErrorMessage = ex.Message;
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostLifecycle(
        int subscriptionId,
        SubscriptionLifecycleAction action,
        string? reason,
        CancellationToken cancellationToken)
    {
        try
        {
            var subscription = await _subscriptionService.ApplyLifecycleActionAsync(
                subscriptionId, action, reason, cancellationToken);

            var effective = action == SubscriptionLifecycleAction.CancelAtEndOfPeriod && subscription.DelayedCancelAt.HasValue
                ? $" Effective {subscription.DelayedCancelAt.Value:D}."
                : string.Empty;

            StatusMessage = $"Subscription {subscription.Id} is now {subscription.State}.{effective}";
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            ErrorMessage = ex.Message;
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    /// <summary>
    /// Reloads everything the page renders. Called after every action so what the customer sees is
    /// always the provider's current view, including any state that drifted out of band.
    /// </summary>
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var userName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return;
        }

        try
        {
            Subscriptions = await _subscriptionService.ListSubscriptionsAsync(userName, cancellationToken);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            Subscriptions = Array.Empty<Subscription>();
            ErrorMessage ??= $"Your subscriptions are unavailable right now. {ex.Message}";
            return;
        }

        try
        {
            Plans = await _subscriptionService.ListPlansAsync(cancellationToken);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            // Losing the plan list only costs the plan-change panel; the rest of the page stands.
            Plans = Array.Empty<SubscriptionPlan>();
            ErrorMessage ??= $"Plan options are unavailable right now. {ex.Message}";
        }

        var usage = new Dictionary<int, int?>();
        foreach (var subscription in Subscriptions.Where(s => s.IsActive))
        {
            try
            {
                usage[subscription.Id] = await _subscriptionService.GetPeriodToDateUnitsAsync(
                    subscription.Id, cancellationToken);
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                // A missing usage figure must not take the whole page down.
                usage[subscription.Id] = null;
            }
        }

        UsageBySubscription = usage;
    }

    private static bool IsExpected(Exception ex) =>
        ex is BillingProviderException or BillingConfigurationException or InvalidSubscriptionOperationException;
}
