using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Web.ViewModels.Subscriptions;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// The customer's subscription management surface: view state and usage (UC2), change plan with a
/// proration preview (UC3), and run the lifecycle actions (UC4).
/// </summary>
[Authorize]
public class MineModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;

    public MineModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public IReadOnlyCollection<SubscriptionViewModel> Subscriptions { get; private set; } = Array.Empty<SubscriptionViewModel>();

    /// <summary>The pending proration preview awaiting the customer's confirmation, if any.</summary>
    public PlanChangePreviewViewModel? PendingPreview { get; private set; }

    public string? ErrorMessage { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostPreviewAsync(long subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken)
    {
        var buyerId = RequireBuyerId();

        return await RunAsync(async () =>
        {
            var preview = await _subscriptionService.PreviewPlanChangeAsync(
                buyerId, subscriptionId, targetPlanHandle, timing, cancellationToken);

            var current = await _subscriptionService.GetSubscriptionForUserAsync(buyerId, subscriptionId, cancellationToken);

            PendingPreview = new PlanChangePreviewViewModel
            {
                SubscriptionId = subscriptionId,
                CurrentPlanHandle = current?.PlanHandle ?? string.Empty,
                TargetPlanHandle = preview.TargetProductHandle,
                Timing = preview.Timing,
                ProratedAdjustment = preview.ProratedAdjustment,
                Charge = preview.Charge,
                PaymentDue = preview.PaymentDue,
                CreditApplied = preview.CreditApplied,
                Fingerprint = preview.Fingerprint
            };
        }, cancellationToken);
    }

    public async Task<IActionResult> OnPostChangePlanAsync(long subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var buyerId = RequireBuyerId();

        try
        {
            var updated = await _subscriptionService.ChangePlanAsync(
                buyerId, subscriptionId, targetPlanHandle, timing, fingerprint, cancellationToken);

            StatusMessage = timing == PlanChangeTiming.Immediate
                ? $"Your plan is now {updated.Billing.ProductName}."
                : $"Your plan changes to '{targetPlanHandle}' at your next renewal.";

            return RedirectToPage();
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            await LoadAsync(cancellationToken);
            ErrorMessage = ex.Message;
            return Page();
        }
    }

    public async Task<IActionResult> OnPostLifecycleAsync(long subscriptionId,
        SubscriptionLifecycleAction action,
        CancellationTiming cancellationTiming,
        string? reason,
        CancellationToken cancellationToken)
    {
        var buyerId = RequireBuyerId();

        try
        {
            var updated = await _subscriptionService.ApplyLifecycleActionAsync(
                buyerId, subscriptionId, action, cancellationTiming, reason, cancellationToken);

            StatusMessage = action == SubscriptionLifecycleAction.Cancel && cancellationTiming == CancellationTiming.EndOfPeriod
                ? $"Your subscription will end on {updated.Billing.DelayedCancelAt?.ToString("D") ?? "the end of the current period"}."
                : $"Your subscription is now {updated.State}.";

            return RedirectToPage();
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            await LoadAsync(cancellationToken);
            ErrorMessage = ex.Message;
            return Page();
        }
    }

    public async Task<IActionResult> OnPostUsageAsync(long subscriptionId,
        int quantity,
        string? memo,
        CancellationToken cancellationToken)
    {
        RequireBuyerId();

        try
        {
            var result = await _subscriptionService.RecordUsageForSubscriptionAsync(subscriptionId, quantity, memo, cancellationToken);

            StatusMessage = result.PeriodToDateAvailable
                ? $"Recorded {result.Quantity} unit(s). {result.PeriodToDateUnits} unit(s) so far this period will appear on your next renewal invoice."
                : $"Recorded {result.Quantity} unit(s). The running total is unavailable right now; it will appear on your next renewal invoice.";

            return RedirectToPage();
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            await LoadAsync(cancellationToken);
            ErrorMessage = ex.Message;
            return Page();
        }
    }

    private async Task<IActionResult> RunAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);

        try
        {
            await action();
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            ErrorMessage = ex.Message;
        }

        return Page();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var buyerId = RequireBuyerId();

        try
        {
            var subscriptions = await _subscriptionService.GetSubscriptionsForUserAsync(buyerId, cancellationToken);
            var plans = await _subscriptionService.ListPlansAsync(cancellationToken);

            var views = new List<SubscriptionViewModel>();
            foreach (var subscription in subscriptions)
            {
                var alternate = plans.FirstOrDefault(p =>
                    !string.Equals(p.Handle, subscription.PlanHandle, StringComparison.OrdinalIgnoreCase));

                var view = new SubscriptionViewModel
                {
                    Id = subscription.ProviderSubscriptionId,
                    PlanHandle = subscription.PlanHandle,
                    PlanName = subscription.Billing.ProductName,
                    Price = subscription.Billing.ProductPrice,
                    State = subscription.State,
                    IsActive = subscription.IsActive,
                    NextBillingDate = subscription.Billing.NextAssessmentAt,
                    CurrentPeriodEndsAt = subscription.Billing.CurrentPeriodEndsAt,
                    CancelAtEndOfPeriod = subscription.Billing.CancelAtEndOfPeriod,
                    DelayedCancelAt = subscription.Billing.DelayedCancelAt,
                    NextPlanHandle = subscription.Billing.NextProductHandle,
                    AlternatePlanHandle = alternate?.Handle,
                    AlternatePlanName = alternate?.Name
                };

                if (subscription.IsActive)
                {
                    await ApplyUsageAsync(view, cancellationToken);
                }

                views.Add(view);
            }

            Subscriptions = views;
        }
        catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException)
        {
            Subscriptions = Array.Empty<SubscriptionViewModel>();
            ErrorMessage = $"Your subscriptions are unavailable right now. {ex.Message}";
        }
    }

    /// <summary>
    /// Adds the usage panel. A usage read failure must not hide the subscription itself, so it is
    /// reported as an unavailable total rather than failing the page.
    /// </summary>
    private async Task ApplyUsageAsync(SubscriptionViewModel view, CancellationToken cancellationToken)
    {
        try
        {
            var usage = await _subscriptionService.GetUsageSummaryAsync(view.Id, cancellationToken);
            view.UsageComponentHandle = usage.ComponentHandle;
            view.PeriodToDateUnits = usage.PeriodToDateUnits;
            view.UsageUnitPrice = usage.UnitPrice;
            view.EstimatedUsageCharge = usage.PeriodToDateEstimatedCharge;
        }
        catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException)
        {
            view.PeriodToDateUnits = null;
        }
    }

    private string RequireBuyerId()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        return User.Identity.Name!;
    }

    private static bool IsExpected(Exception ex) =>
        ex is BillingProviderException or BillingConfigurationException or InvalidSubscriptionOperationException;
}
