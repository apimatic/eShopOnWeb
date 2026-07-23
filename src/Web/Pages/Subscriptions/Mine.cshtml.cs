using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// The customer's subscription management surface: view state and next billing date (UC1), report
/// pay-as-you-go usage (UC2), preview and commit a plan change (UC3), and run the lifecycle
/// actions (UC4).
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

    public Subscription? LiveSubscription { get; private set; }

    /// <summary>The preview the customer must confirm before a plan change is committed (UC3, step 3).</summary>
    public PlanChangePreview? Preview { get; private set; }

    public UsageReport? UsageReport { get; private set; }

    public string? StatusMessage { get; private set; }

    public string? ErrorMessage { get; private set; }

    public async Task OnGet()
    {
        await LoadAsync();
    }

    public Task<IActionResult> OnPostRecordUsage(decimal quantity, string? memo)
    {
        return ExecuteAsync(async userName =>
        {
            UsageReport = await _subscriptionService.RecordUsageAsync(userName, quantity, memo);
            StatusMessage = $"Recorded {UsageReport.Record.Quantity:N0} API call(s). "
                + (UsageReport.PeriodToDateTotal.HasValue
                    ? $"Period to date: {UsageReport.PeriodToDateTotal.Value:N0} units (about $ {UsageReport.EstimatedCharge:N2}), billed on your next renewal invoice."
                    : "The running total is currently unavailable, but the usage was recorded.");
        });
    }

    public Task<IActionResult> OnPostPreviewPlanChange(string targetPlanHandle, PlanChangeTiming timing)
    {
        return ExecuteAsync(async userName =>
        {
            Preview = await _subscriptionService.PreviewPlanChangeAsync(userName, targetPlanHandle, timing);
        });
    }

    public Task<IActionResult> OnPostChangePlan(string targetPlanHandle, PlanChangeTiming timing, int previewedPaymentDueInCents)
    {
        return ExecuteAsync(async userName =>
        {
            var subscription = await _subscriptionService.ChangePlanAsync(userName, targetPlanHandle, timing, previewedPaymentDueInCents);
            StatusMessage = timing == PlanChangeTiming.Immediate
                ? $"Your plan is now {subscription.Plan.Name}."
                : $"Your plan changes to '{targetPlanHandle}' on {subscription.CurrentPeriodEndsAt:d}.";
        });
    }

    public Task<IActionResult> OnPostPause()
    {
        return ExecuteAsync(async userName =>
        {
            var subscription = await _subscriptionService.PauseAsync(userName);
            StatusMessage = $"Your subscription is paused ({subscription.ProviderState}).";
        });
    }

    public Task<IActionResult> OnPostResume()
    {
        return ExecuteAsync(async userName =>
        {
            var subscription = await _subscriptionService.ResumeAsync(userName);
            StatusMessage = $"Your subscription is active again. Next billing date {subscription.NextAssessmentAt:d}.";
        });
    }

    public Task<IActionResult> OnPostCancel(CancellationTiming timing, string? reason)
    {
        return ExecuteAsync(async userName =>
        {
            var subscription = await _subscriptionService.CancelAsync(userName, timing, reason);
            StatusMessage = subscription.CancelAtEndOfPeriod
                ? $"Your subscription will be cancelled on {subscription.DelayedCancelAt:d}."
                : "Your subscription has been cancelled.";
        });
    }

    public Task<IActionResult> OnPostReactivate()
    {
        return ExecuteAsync(async userName =>
        {
            var subscription = await _subscriptionService.ReactivateAsync(userName);
            StatusMessage = $"Your subscription is active again on {subscription.Plan.Name}.";
        });
    }

    /// <summary>
    /// Runs one management action, surfacing a rejection or a provider failure as a message on the
    /// page rather than an unhandled error, then re-reads the subscription from the provider.
    /// </summary>
    private async Task<IActionResult> ExecuteAsync(Func<string, Task> action)
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));

        try
        {
            await action(User.Identity.Name!);
        }
        catch (Exception ex) when (ex is BillingProviderException or InvalidSubscriptionOperationException)
        {
            ErrorMessage = ex.Message;
        }

        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));

        try
        {
            // The provider is the system of record, so the view is always re-read from it — it may
            // otherwise lag out-of-band changes made in the Maxio UI.
            Subscriptions = await _subscriptionService.GetSubscriptionsForUserAsync(User.Identity.Name!);
            LiveSubscription = Subscriptions.FirstOrDefault(s => s.IsLive) ?? Subscriptions.FirstOrDefault();
            Plans = await _subscriptionService.ListPlansAsync();
        }
        catch (BillingProviderException ex)
        {
            ErrorMessage ??= ex.Message;
        }
    }
}
