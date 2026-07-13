using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Web.ViewModels.Subscriptions;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// UC1/UC2/UC3/UC4 management surface — view subscriptions, report usage, preview/commit a plan
/// change, and run lifecycle actions. Mirror <c>OrderController.MyOrders</c>.
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

    public List<SubscriptionViewModel> Subscriptions { get; set; } = new();
    public List<PlanViewModel> Plans { get; set; } = new();
    public PlanChangePreviewViewModel? Preview { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostRecordUsageAsync(long subscriptionId, double quantity, string? memo)
    {
        try
        {
            var result = await _subscriptionService.RecordUsageAsync(GetUsername(), IsAdmin(), subscriptionId, quantity, memo);
            StatusMessage = result.PeriodToDateTotal.HasValue
                ? $"Recorded {result.QuantityRecorded} unit(s) of usage. Period-to-date total: {result.PeriodToDateTotal}."
                : $"Recorded {result.QuantityRecorded} unit(s) of usage. Period-to-date total is currently unavailable.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning("RecordUsage failed for subscription {0}: {1}", subscriptionId, ex.Message);
            StatusMessage = $"Could not record usage: {ex.Message}";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPreviewPlanChangeAsync(long subscriptionId, string targetProductHandle)
    {
        try
        {
            var preview = await _subscriptionService.PreviewPlanChangeAsync(
                GetUsername(), IsAdmin(), subscriptionId, targetProductHandle, PlanChangeTiming.Immediate);

            Preview = new PlanChangePreviewViewModel
            {
                SubscriptionId = preview.SubscriptionId,
                FromProductHandle = preview.FromProductHandle,
                ToProductHandle = preview.ToProductHandle,
                ProratedAdjustment = preview.ProratedAdjustmentInCents / 100m,
                ProratedAdjustmentInCents = preview.ProratedAdjustmentInCents,
                Charge = preview.ChargeInCents / 100m,
                PaymentDue = preview.PaymentDueInCents / 100m,
                CreditApplied = preview.CreditAppliedInCents / 100m
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning("PreviewPlanChange failed for subscription {0}: {1}", subscriptionId, ex.Message);
            StatusMessage = $"Could not preview plan change: {ex.Message}";
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCommitPlanChangeAsync(long subscriptionId, string targetProductHandle, long expectedProratedAdjustmentInCents)
    {
        try
        {
            var updated = await _subscriptionService.CommitPlanChangeAsync(
                GetUsername(), IsAdmin(), subscriptionId, targetProductHandle, PlanChangeTiming.Immediate, expectedProratedAdjustmentInCents);
            StatusMessage = $"Subscription moved to {updated.ProductName}.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning("CommitPlanChange failed for subscription {0}: {1}", subscriptionId, ex.Message);
            StatusMessage = $"Could not change plan: {ex.Message}";
        }

        return RedirectToPage();
    }

    public Task<IActionResult> OnPostPauseAsync(long subscriptionId) =>
        RunLifecycleActionAsync(subscriptionId, () => _subscriptionService.PauseSubscriptionAsync(GetUsername(), IsAdmin(), subscriptionId), "paused");

    public Task<IActionResult> OnPostResumeAsync(long subscriptionId) =>
        RunLifecycleActionAsync(subscriptionId, () => _subscriptionService.ResumeSubscriptionAsync(GetUsername(), IsAdmin(), subscriptionId), "resumed");

    public Task<IActionResult> OnPostReactivateAsync(long subscriptionId) =>
        RunLifecycleActionAsync(subscriptionId, () => _subscriptionService.ReactivateSubscriptionAsync(GetUsername(), IsAdmin(), subscriptionId), "reactivated");

    public Task<IActionResult> OnPostCancelAsync(long subscriptionId, bool endOfPeriod, string? reason) =>
        RunLifecycleActionAsync(
            subscriptionId,
            () => _subscriptionService.CancelSubscriptionAsync(GetUsername(), IsAdmin(), subscriptionId, endOfPeriod, reason),
            endOfPeriod ? "scheduled for cancellation at the end of the period" : "cancelled");

    private async Task<IActionResult> RunLifecycleActionAsync(long subscriptionId, Func<Task<BillingSubscription>> action, string verbPhrase)
    {
        try
        {
            await action();
            StatusMessage = $"Subscription {verbPhrase}.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Lifecycle action failed for subscription {0}: {1}", subscriptionId, ex.Message);
            StatusMessage = $"Could not update subscription: {ex.Message}";
        }

        return RedirectToPage();
    }

    private string GetUsername()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        return User.Identity!.Name!;
    }

    private bool IsAdmin() => User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);

    private async Task LoadAsync()
    {
        var subscriptions = await _subscriptionService.GetSubscriptionsForCustomerAsync(GetUsername());
        Subscriptions = subscriptions.Select(s => new SubscriptionViewModel
        {
            Id = s.Id,
            ProductHandle = s.ProductHandle,
            ProductName = s.ProductName,
            Price = s.ProductPriceInCents / 100m,
            State = s.State.ToString(),
            BalanceInDollars = s.BalanceInCents / 100m,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            NextAssessmentAt = s.NextAssessmentAt
        }).ToList();

        var plans = await _subscriptionService.ListPlansAsync();
        Plans = plans.Select(p => new PlanViewModel
        {
            Id = p.Id,
            Handle = p.Handle,
            Name = p.Name,
            Price = p.PriceInCents / 100m,
            RequiresPaymentMethod = p.RequiresPaymentMethod
        }).ToList();
    }
}
