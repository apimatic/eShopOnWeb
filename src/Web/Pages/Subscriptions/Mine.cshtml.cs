using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// Customer's own subscription management surface (UC2/UC3/UC4). Mirrors OrderController.MyOrders,
/// but as a Razor Page since it needs POST form handlers for the lifecycle/usage/plan-change actions.
/// </summary>
public class MineModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;

    public MineModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public IReadOnlyList<BillingSubscription> Subscriptions { get; set; } = Array.Empty<BillingSubscription>();
    public IReadOnlyList<BillingPlan> Plans { get; set; } = Array.Empty<BillingPlan>();
    public Dictionary<int, BillingComponentBalance> UsageBalances { get; } = new();
    public Dictionary<int, BillingPlanChangePreview> Previews { get; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    private string CustomerReference => Guard.Against.NullOrEmpty(User.Identity?.Name, nameof(User.Identity.Name))!;

    public async Task OnGet()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostPreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle)
    {
        await LoadAsync();

        try
        {
            Previews[subscriptionId] = await _subscriptionService.PreviewPlanChangeAsync(CustomerReference, subscriptionId, targetPlanHandle, isAdmin: false);
        }
        catch (Exception ex) when (IsExpectedSubscriptionException(ex))
        {
            ErrorMessage = ex.Message;
        }

        return Page();
    }

    public Task<IActionResult> OnPostConfirmPlanChangeNowAsync(
        int subscriptionId,
        string targetPlanHandle,
        long proratedAdjustmentInCents,
        long chargeInCents,
        long paymentDueInCents,
        long creditAppliedInCents,
        DateTimeOffset effectiveAt)
    {
        var confirmedPreview = new BillingPlanChangePreview(
            targetPlanHandle, proratedAdjustmentInCents, chargeInCents, paymentDueInCents, creditAppliedInCents, effectiveAt);

        return RunActionAsync(
            () => _subscriptionService.CommitPlanChangeAsync(CustomerReference, subscriptionId, targetPlanHandle, PlanChangeTiming.Now, confirmedPreview, isAdmin: false),
            "Plan change applied.");
    }

    public Task<IActionResult> OnPostSchedulePlanChangeAsync(int subscriptionId, string targetPlanHandle) =>
        RunActionAsync(
            () => _subscriptionService.CommitPlanChangeAsync(CustomerReference, subscriptionId, targetPlanHandle, PlanChangeTiming.AtRenewal, null, isAdmin: false),
            "Plan change scheduled for your next renewal - no charge until then.");

    public Task<IActionResult> OnPostPauseAsync(int subscriptionId) =>
        RunActionAsync(() => _subscriptionService.PauseAsync(CustomerReference, subscriptionId, isAdmin: false), "Subscription paused.");

    public Task<IActionResult> OnPostResumeAsync(int subscriptionId) =>
        RunActionAsync(() => _subscriptionService.ResumeAsync(CustomerReference, subscriptionId, isAdmin: false), "Subscription resumed.");

    public Task<IActionResult> OnPostCancelAsync(int subscriptionId, bool endOfPeriod) =>
        RunActionAsync(
            () => _subscriptionService.CancelAsync(CustomerReference, subscriptionId, endOfPeriod, reason: null, isAdmin: false),
            endOfPeriod ? "Cancellation scheduled for the end of the current period." : "Subscription cancelled.");

    public Task<IActionResult> OnPostReactivateAsync(int subscriptionId) =>
        RunActionAsync(() => _subscriptionService.ReactivateAsync(CustomerReference, subscriptionId, isAdmin: false), "Subscription reactivated.");

    public async Task<IActionResult> OnPostRecordUsageAsync(int subscriptionId, double quantity)
    {
        try
        {
            var usage = await _subscriptionService.RecordUsageAsync(CustomerReference, subscriptionId, quantity, memo: null, isAdmin: false);
            StatusMessage = usage.PeriodToDateBalance.HasValue
                ? $"Recorded {quantity:N0} unit(s) of usage. Period-to-date total: {usage.PeriodToDateBalance}."
                : $"Recorded {quantity:N0} unit(s) of usage.";
        }
        catch (Exception ex) when (IsExpectedSubscriptionException(ex))
        {
            ErrorMessage = ex.Message;
        }

        return RedirectToPage();
    }

    private async Task<IActionResult> RunActionAsync(Func<Task<BillingSubscription>> action, string successMessage)
    {
        try
        {
            await action();
            StatusMessage = successMessage;
        }
        catch (Exception ex) when (IsExpectedSubscriptionException(ex))
        {
            ErrorMessage = ex.Message;
        }

        return RedirectToPage();
    }

    private static bool IsExpectedSubscriptionException(Exception ex) =>
        ex is BillingProviderException
            or BillingConfigurationException
            or InvalidSubscriptionStateException
            or PlanChangePreviewStaleException
            or SubscriptionNotFoundException
            or ArgumentException;

    private async Task LoadAsync()
    {
        Subscriptions = await _subscriptionService.ListMySubscriptionsAsync(CustomerReference);

        try
        {
            Plans = await _subscriptionService.ListPlansAsync();
        }
        catch (BillingProviderException)
        {
            Plans = Array.Empty<BillingPlan>();
        }

        foreach (var subscription in Subscriptions.Where(s => s.State == BillingSubscriptionState.Active))
        {
            try
            {
                UsageBalances[subscription.Id] = await _subscriptionService.GetUsageBalanceAsync(CustomerReference, subscription.Id, isAdmin: false);
            }
            catch (BillingProviderException)
            {
                // Balance display is best-effort - the subscriptions list itself still renders.
            }
        }
    }
}
