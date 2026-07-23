using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// The customer's subscription management surface: their subscriptions and next billing date (UC1),
/// a usage panel (UC2), plan change with a proration preview (UC3), and the four lifecycle actions
/// (UC4).
/// </summary>
[Authorize]
public class MineModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;

    public MineModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public IReadOnlyCollection<Subscription> Subscriptions { get; set; } = Array.Empty<Subscription>();

    public IReadOnlyCollection<SubscriptionPlan> Plans { get; set; } = Array.Empty<SubscriptionPlan>();

    public PlanChangePreview? Preview { get; set; }

    public UsageReport? UsageReport { get; set; }

    public string? ErrorMessage { get; set; }

    public string? StatusMessage { get; set; }

    public async Task OnGet(int? subscribed)
    {
        if (subscribed.HasValue)
        {
            StatusMessage = $"Subscription {subscribed.Value} is active.";
        }

        await LoadAsync();
    }

    public Task<IActionResult> OnPostRecordUsage(int subscriptionId, decimal quantity) =>
        ExecuteAsync(async () =>
        {
            UsageReport = await _subscriptionService.RecordUsageAsync(subscriptionId, quantity, "Reported from the storefront");

            StatusMessage = UsageReport.IsSummaryAvailable
                ? $"Recorded {UsageReport.Record.Quantity} unit(s). Period-to-date total is {UsageReport.Summary!.UnitBalance}; it will appear on your next renewal invoice."
                : $"Recorded {UsageReport.Record.Quantity} unit(s). The running total is unavailable right now; it will appear on your next renewal invoice.";
        });

    public Task<IActionResult> OnPostPreviewPlanChange(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing) =>
        ExecuteAsync(async () =>
        {
            Preview = await _subscriptionService.PreviewPlanChangeAsync(subscriptionId, targetPlanHandle, timing);
        });

    public Task<IActionResult> OnPostChangePlan(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing,
        decimal expectedNetAmount) =>
        ExecuteAsync(async () =>
        {
            var subscription = await _subscriptionService.ChangePlanAsync(subscriptionId, targetPlanHandle, timing,
                expectedNetAmount);

            StatusMessage = timing == PlanChangeTiming.Immediate
                ? $"Subscription {subscription.Id} is now on {subscription.PlanName}. The prorated amount of {expectedNetAmount:N2} was applied."
                : $"Subscription {subscription.Id} will move to {targetPlanHandle} on {subscription.CurrentPeriodEndsAt:d}.";
        });

    public Task<IActionResult> OnPostPause(int subscriptionId) =>
        ExecuteAsync(async () =>
        {
            var subscription = await _subscriptionService.PauseAsync(subscriptionId);
            StatusMessage = $"Subscription {subscription.Id} is now {subscription.State}.";
        });

    public Task<IActionResult> OnPostResume(int subscriptionId) =>
        ExecuteAsync(async () =>
        {
            var subscription = await _subscriptionService.ResumeAsync(subscriptionId);
            StatusMessage = $"Subscription {subscription.Id} is now {subscription.State}.";
        });

    public Task<IActionResult> OnPostCancel(int subscriptionId, CancellationTiming timing) =>
        ExecuteAsync(async () =>
        {
            var subscription = await _subscriptionService.CancelAsync(subscriptionId, timing, "Cancelled from the storefront");

            StatusMessage = timing == CancellationTiming.EndOfPeriod
                ? $"Subscription {subscription.Id} will cancel on {subscription.DelayedCancelAt:d}."
                : $"Subscription {subscription.Id} is now {subscription.State}.";
        });

    public Task<IActionResult> OnPostReactivate(int subscriptionId) =>
        ExecuteAsync(async () =>
        {
            var subscription = await _subscriptionService.ReactivateAsync(subscriptionId);
            StatusMessage = $"Subscription {subscription.Id} is now {subscription.State}.";
        });

    /// <summary>
    /// Runs an action, turning a rejected operation or a provider failure into a message on the page
    /// rather than an unhandled error, then re-reads the subscriptions so the customer always sees
    /// the provider's current view.
    /// </summary>
    private async Task<IActionResult> ExecuteAsync(Func<Task> action)
    {
        try
        {
            await action();
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
        Guard.Against.Null(User.Identity, nameof(User.Identity));
        Guard.Against.NullOrEmpty(User.Identity!.Name, nameof(User.Identity.Name));

        try
        {
            Subscriptions = await _subscriptionService.ListSubscriptionsAsync(User.Identity.Name!);
            Plans = await _subscriptionService.ListPlansAsync();
        }
        catch (BillingProviderException ex)
        {
            ErrorMessage ??= $"Your subscriptions are unavailable right now. {ex.Message}";
        }
    }
}
