using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// The customer's subscription management surface: current subscriptions and their state (UC1),
/// the pay-as-you-go usage panel (UC2), plan change with a proration preview (UC3), and the
/// lifecycle actions (UC4).
/// </summary>
[Authorize]
public class MineModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;

    public MineModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public IReadOnlyCollection<Subscription> Subscriptions { get; private set; } = Array.Empty<Subscription>();

    public IReadOnlyCollection<SubscriptionPlan> Plans { get; private set; } = Array.Empty<SubscriptionPlan>();

    /// <summary>The running metered usage for the customer's active subscription, when there is one.</summary>
    public UsageReport? Usage { get; private set; }

    /// <summary>A plan change awaiting confirmation. Rendered as a confirm form, never auto-applied.</summary>
    public PlanChangePreview? PendingPreview { get; private set; }

    public string? ErrorMessage { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public Subscription? ActiveSubscription => Subscriptions.FirstOrDefault(s => s.IsActive);

    public async Task OnGet(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostRecordUsage(int subscriptionId, decimal quantity, string? memo, CancellationToken cancellationToken)
    {
        return await ExecuteAsync(async () =>
        {
            var report = await _subscriptionService.RecordUsageAsync(
                subscriptionId, GetUserReference(), quantity, memo, cancellationToken);

            StatusMessage = report.PeriodToDateAvailable
                ? $"Recorded {report.Record.Quantity:N0} unit(s). Period to date: {report.PeriodToDateUnits:N0} unit(s)" +
                  $"{(report.PeriodToDateAmount.HasValue ? $" (${report.PeriodToDateAmount.Value:N2})" : string.Empty)}, billed on your next renewal invoice."
                : $"Recorded {report.Record.Quantity:N0} unit(s). The running period-to-date total is currently unavailable.";
        }, cancellationToken);
    }

    public async Task<IActionResult> OnPostPreviewPlanChange(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing, CancellationToken cancellationToken)
    {
        try
        {
            PendingPreview = await _subscriptionService.PreviewPlanChangeAsync(
                subscriptionId, GetUserReference(), targetPlanHandle, timing, cancellationToken);
        }
        catch (Exception ex) when (ex is BillingProviderException or InvalidSubscriptionOperationException)
        {
            ErrorMessage = ex.Message;
        }

        await LoadAsync(cancellationToken);

        return Page();
    }

    public async Task<IActionResult> OnPostChangePlan(int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        string previewToken,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(async () =>
        {
            var subscription = await _subscriptionService.ChangePlanAsync(
                subscriptionId, GetUserReference(), targetPlanHandle, timing, previewToken, cancellationToken);

            StatusMessage = timing == PlanChangeTiming.Immediate
                ? $"Your subscription is now on {subscription.PlanName}."
                : $"Your subscription moves to {targetPlanHandle} at the end of the current period.";
        }, cancellationToken);
    }

    public async Task<IActionResult> OnPostLifecycle(int subscriptionId,
        SubscriptionLifecycleAction action,
        CancellationTiming cancellationTiming,
        string? reason,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(async () =>
        {
            var subscription = await _subscriptionService.ApplyLifecycleActionAsync(
                subscriptionId, GetUserReference(), action, cancellationTiming, reason, cancellationToken);

            var effective = subscription.CancelAtEndOfPeriod && subscription.DelayedCancelAt.HasValue
                ? $" Effective {subscription.DelayedCancelAt.Value:d}."
                : string.Empty;

            StatusMessage = $"Subscription {subscription.Id} is now {subscription.State}.{effective}";
        }, cancellationToken);
    }

    /// <summary>
    /// Runs a write action, redirecting on success (post/redirect/get) and re-rendering the page with
    /// a friendly message when the request is rejected or the provider fails.
    /// </summary>
    private async Task<IActionResult> ExecuteAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        try
        {
            await action();

            return RedirectToPage();
        }
        catch (Exception ex) when (ex is BillingProviderException or InvalidSubscriptionOperationException)
        {
            ErrorMessage = ex.Message;
            await LoadAsync(cancellationToken);

            return Page();
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            Subscriptions = await _subscriptionService.ListSubscriptionsAsync(GetUserReference(), cancellationToken);
            Plans = await _subscriptionService.ListPlansAsync(cancellationToken);

            var active = ActiveSubscription;
            if (active is not null)
            {
                Usage = await _subscriptionService.GetUsageSummaryAsync(active.Id, GetUserReference(), cancellationToken);
            }
        }
        catch (BillingProviderException ex)
        {
            // The provider is unreachable or misconfigured; show what we know rather than a 500.
            ErrorMessage ??= $"Your subscriptions are unavailable right now. {ex.Message}";
        }
    }

    private string GetUserReference()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));

        return User.Identity.Name!;
    }
}
