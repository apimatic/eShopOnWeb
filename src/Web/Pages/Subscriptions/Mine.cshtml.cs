using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

[Authorize]
public class MineModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;

    public MineModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public IReadOnlyCollection<CustomerSubscription> Subscriptions { get; set; } = new List<CustomerSubscription>();

    public IReadOnlyCollection<SubscriptionPlan> Plans { get; set; } = new List<SubscriptionPlan>();

    /// <summary>The proration the customer must see and confirm before a plan change is committed.</summary>
    public PlanChangePreview? Preview { get; set; }

    public int PreviewSubscriptionId { get; set; }

    public string? ErrorMessage { get; set; }

    public string? StatusMessage { get; set; }

    public async Task OnGet(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostPreviewPlanChange(int subscriptionId, string targetPlanHandle,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(async () =>
        {
            Preview = await _subscriptionService.PreviewPlanChangeAsync(subscriptionId, targetPlanHandle,
                cancellationToken);
            PreviewSubscriptionId = subscriptionId;
        }, cancellationToken);
    }

    public async Task<IActionResult> OnPostChangePlan(int subscriptionId, string targetPlanHandle,
        PlanChangeTiming timing, decimal? previewedPaymentDue, CancellationToken cancellationToken)
    {
        return await ExecuteAsync(async () =>
        {
            var changed = await _subscriptionService.ChangePlanAsync(subscriptionId, targetPlanHandle, timing,
                previewedPaymentDue, cancellationToken);

            StatusMessage = timing == PlanChangeTiming.Immediately
                ? $"Moved to {changed.PlanName}, effective now."
                : $"Scheduled a move to {targetPlanHandle}, effective {Format(changed.CurrentPeriodEndsAt)}.";
        }, cancellationToken);
    }

    public async Task<IActionResult> OnPostPause(int subscriptionId, CancellationToken cancellationToken)
    {
        return await ExecuteAsync(async () =>
        {
            var paused = await _subscriptionService.PauseAsync(subscriptionId, cancellationToken);
            StatusMessage = $"Subscription {paused.Id} is now {paused.State}.";
        }, cancellationToken);
    }

    public async Task<IActionResult> OnPostResume(int subscriptionId, CancellationToken cancellationToken)
    {
        return await ExecuteAsync(async () =>
        {
            var resumed = await _subscriptionService.ResumeAsync(subscriptionId, cancellationToken);
            StatusMessage = $"Subscription {resumed.Id} is now {resumed.State}.";
        }, cancellationToken);
    }

    public async Task<IActionResult> OnPostCancel(int subscriptionId, CancellationTiming timing, string? reason,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(async () =>
        {
            var cancelled = await _subscriptionService.CancelAsync(subscriptionId, timing, reason, cancellationToken);

            StatusMessage = cancelled.CancelAtEndOfPeriod
                ? $"Subscription {cancelled.Id} will cancel on {Format(cancelled.DelayedCancelAt ?? cancelled.CurrentPeriodEndsAt)}."
                : $"Subscription {cancelled.Id} is now {cancelled.State}.";
        }, cancellationToken);
    }

    public async Task<IActionResult> OnPostReactivate(int subscriptionId, CancellationToken cancellationToken)
    {
        return await ExecuteAsync(async () =>
        {
            var reactivated = await _subscriptionService.ReactivateAsync(subscriptionId, cancellationToken);
            StatusMessage = $"Subscription {reactivated.Id} is now {reactivated.State}.";
        }, cancellationToken);
    }

    public async Task<IActionResult> OnPostRecordUsage(int subscriptionId, decimal quantity, string? memo,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(async () =>
        {
            var report = await _subscriptionService.RecordUsageForSubscriptionAsync(subscriptionId, quantity, memo,
                cancellationToken);

            StatusMessage = report.IsPeriodToDateTotalAvailable
                ? $"Recorded {report.RecordedUsage.Quantity} unit(s). {report.PeriodToDateTotal} unit(s) so far this period will appear on your next renewal invoice."
                : $"Recorded {report.RecordedUsage.Quantity} unit(s). The period-to-date total is currently unavailable.";
        }, cancellationToken);
    }

    /// <summary>
    /// Runs a management action and always re-renders the page against the provider's current view,
    /// so a rejected action shows why alongside the real state rather than a stale one.
    /// </summary>
    private async Task<IActionResult> ExecuteAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is BillingProviderException
            or BillingConfigurationException
            or InvalidSubscriptionTransitionException
            or InvalidPlanChangeException
            or StalePlanChangePreviewException
            or SubscriptionNotFoundException
            or NoActiveSubscriptionException)
        {
            ErrorMessage = exception.Message;
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));

        try
        {
            Subscriptions = await _subscriptionService.GetMySubscriptionsAsync(User.Identity.Name, cancellationToken);
            Plans = await _subscriptionService.GetAvailablePlansAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is BillingProviderException or BillingConfigurationException)
        {
            ErrorMessage ??= "Your subscriptions are unavailable right now. Please try again later.";
        }
    }

    private static string Format(DateTimeOffset? value)
    {
        return value.HasValue ? value.Value.ToString("d MMM yyyy") : "the end of the current period";
    }
}
