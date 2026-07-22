using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// The customer's subscription management surface: usage reporting (UC2), plan change with a
/// proration preview (UC3) and the lifecycle actions (UC4).
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

    public decimal? PeriodToDateUsage { get; set; }

    public PlanChangePreview? Preview { get; set; }

    public string? StatusMessage { get; set; }

    public string? ErrorMessage { get; set; }

    private string BuyerId => User.Identity!.Name!;

    public async Task OnGet()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostRecordUsage(int subscriptionId, decimal quantity, string? memo)
    {
        return await ExecuteAsync(async () =>
        {
            var report = await _subscriptionService.RecordUsageAsync(subscriptionId, BuyerId, quantity, memo);

            StatusMessage = report.PeriodToDateTotal.HasValue
                ? $"Recorded {report.Recorded.Quantity} unit(s). {report.PeriodToDateTotal} unit(s) so far this period" +
                  $"{(report.EstimatedPeriodToDateCharge.HasValue ? $" (about {report.EstimatedPeriodToDateCharge:C})" : string.Empty)}" +
                  " will appear on your next renewal invoice."
                : $"Recorded {report.Recorded.Quantity} unit(s). The running total is currently unavailable.";
        });
    }

    public async Task<IActionResult> OnPostPreviewPlanChange(int subscriptionId, string targetPlanHandle,
        PlanChangeTiming timing)
    {
        return await ExecuteAsync(async () =>
        {
            Preview = await _subscriptionService.PreviewPlanChangeAsync(subscriptionId, BuyerId,
                targetPlanHandle, timing);
        });
    }

    public async Task<IActionResult> OnPostChangePlan(int subscriptionId, string targetPlanHandle,
        PlanChangeTiming timing, int proratedAdjustmentInCents, int chargeInCents,
        int paymentDueInCents, int creditAppliedInCents)
    {
        return await ExecuteAsync(async () =>
        {
            // Re-submit exactly what the customer was shown so a moved price is caught (UC3).
            var confirmed = new PlanChangePreview(targetPlanHandle, timing, proratedAdjustmentInCents,
                chargeInCents, paymentDueInCents, creditAppliedInCents);

            var changed = await _subscriptionService.ChangePlanAsync(subscriptionId, BuyerId,
                targetPlanHandle, timing, confirmed);

            StatusMessage = timing == PlanChangeTiming.Immediately
                ? $"Your plan is now {changed.Plan.Name}, effective immediately."
                : $"Your plan changes to {targetPlanHandle} at your next renewal on {changed.CurrentPeriodEndsAt:d}.";
        });
    }

    public async Task<IActionResult> OnPostPause(int subscriptionId)
    {
        return await ExecuteAsync(async () =>
        {
            var paused = await _subscriptionService.PauseAsync(subscriptionId, BuyerId, null);
            StatusMessage = $"Subscription paused; it is now {paused.State}.";
        });
    }

    public async Task<IActionResult> OnPostResume(int subscriptionId)
    {
        return await ExecuteAsync(async () =>
        {
            var resumed = await _subscriptionService.ResumeAsync(subscriptionId, BuyerId);
            StatusMessage = $"Subscription resumed; it is now {resumed.State}.";
        });
    }

    public async Task<IActionResult> OnPostCancel(int subscriptionId, CancellationTiming timing, string? reason)
    {
        return await ExecuteAsync(async () =>
        {
            var canceled = await _subscriptionService.CancelAsync(subscriptionId, BuyerId, timing, reason);

            StatusMessage = timing == CancellationTiming.EndOfPeriod
                ? $"Your subscription will be cancelled on {canceled.CurrentPeriodEndsAt:d}."
                : $"Subscription cancelled; it is now {canceled.State}.";
        });
    }

    public async Task<IActionResult> OnPostReactivate(int subscriptionId)
    {
        return await ExecuteAsync(async () =>
        {
            var reactivated = await _subscriptionService.ReactivateAsync(subscriptionId, BuyerId);
            StatusMessage = $"Subscription reactivated; it is now {reactivated.State}.";
        });
    }

    /// <summary>
    /// Runs one management action, turning the domain's typed failures into a message on the
    /// page rather than an unhandled exception, and always re-rendering fresh provider state.
    /// </summary>
    private async Task<IActionResult> ExecuteAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is BillingProviderException
            or BillingConfigurationException
            or InvalidSubscriptionTransitionException
            or StalePlanChangePreviewException
            or SubscriptionNotFoundException
            or ArgumentException)
        {
            ErrorMessage = ex.Message;
        }

        await LoadAsync();

        return Page();
    }

    private async Task LoadAsync()
    {
        try
        {
            Subscriptions = await _subscriptionService.GetSubscriptionsForUserAsync(BuyerId);
            Plans = await _subscriptionService.GetAvailablePlansAsync();

            var active = Subscriptions.FirstOrDefault(s => s.IsActive);
            if (active != null)
            {
                PeriodToDateUsage = await _subscriptionService.GetPeriodToDateUsageAsync(active.Id, BuyerId);
            }
        }
        catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException)
        {
            ErrorMessage ??= ex.Message;
        }
    }
}
