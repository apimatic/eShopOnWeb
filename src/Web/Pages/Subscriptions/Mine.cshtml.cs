using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

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

    public IReadOnlyList<BillingSubscription> Subscriptions { get; set; } = Array.Empty<BillingSubscription>();
    public IReadOnlyList<BillingPlan> AvailablePlans { get; set; } = Array.Empty<BillingPlan>();
    public string? ErrorMessage { get; set; }
    public string? StatusMessage { get; set; }

    /// <summary>The subscription a plan-change preview was just requested for, so the confirm form renders under it.</summary>
    public int? PreviewedSubscriptionId { get; set; }
    public string? PreviewedTargetProductHandle { get; set; }
    public PlanChangePreview? Preview { get; set; }

    public async Task OnGet()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostRecordUsageAsync(int subscriptionId, decimal quantity, string? memo)
    {
        var userName = RequireUserName();
        try
        {
            var result = await _subscriptionService.RecordUsageAsync(subscriptionId, quantity, memo, userName);
            StatusMessage = result.PeriodToDateBalance.HasValue
                ? $"Recorded {quantity} unit(s) of usage. Period-to-date balance: {result.PeriodToDateBalance}."
                : $"Recorded {quantity} unit(s) of usage. The period-to-date balance could not be read back.";
        }
        catch (Exception ex) when (IsExpectedSubscriptionError(ex))
        {
            ErrorMessage = ex.Message;
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostPauseAsync(int subscriptionId)
    {
        var userName = RequireUserName();
        try
        {
            await _subscriptionService.PauseAsync(subscriptionId, userName);
            StatusMessage = $"Subscription {subscriptionId} paused.";
        }
        catch (Exception ex) when (IsExpectedSubscriptionError(ex))
        {
            ErrorMessage = ex.Message;
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostResumeAsync(int subscriptionId)
    {
        var userName = RequireUserName();
        try
        {
            await _subscriptionService.ResumeAsync(subscriptionId, userName);
            StatusMessage = $"Subscription {subscriptionId} resumed.";
        }
        catch (Exception ex) when (IsExpectedSubscriptionError(ex))
        {
            ErrorMessage = ex.Message;
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCancelAsync(int subscriptionId, bool endOfPeriod, string? reason)
    {
        var userName = RequireUserName();
        try
        {
            await _subscriptionService.CancelAsync(subscriptionId, endOfPeriod, reason, userName);
            StatusMessage = endOfPeriod
                ? $"Subscription {subscriptionId} will cancel at the end of the current period."
                : $"Subscription {subscriptionId} cancelled immediately.";
        }
        catch (Exception ex) when (IsExpectedSubscriptionError(ex))
        {
            ErrorMessage = ex.Message;
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostReactivateAsync(int subscriptionId)
    {
        var userName = RequireUserName();
        try
        {
            await _subscriptionService.ReactivateAsync(subscriptionId, userName);
            StatusMessage = $"Subscription {subscriptionId} reactivated.";
        }
        catch (Exception ex) when (IsExpectedSubscriptionError(ex))
        {
            ErrorMessage = ex.Message;
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostPreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, bool applyNow)
    {
        var userName = RequireUserName();
        try
        {
            Preview = await _subscriptionService.PreviewPlanChangeAsync(subscriptionId, targetProductHandle, applyNow, userName);
            PreviewedSubscriptionId = subscriptionId;
            PreviewedTargetProductHandle = targetProductHandle;
        }
        catch (Exception ex) when (IsExpectedSubscriptionError(ex))
        {
            ErrorMessage = ex.Message;
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCommitPlanChangeAsync(
        int subscriptionId,
        string targetProductHandle,
        bool applyNow,
        long targetPriceInCents,
        long? proratedAdjustmentInCents,
        long? chargeInCents,
        long? paymentDueInCents,
        long? creditAppliedInCents)
    {
        var userName = RequireUserName();
        var shownPreview = new PlanChangePreview(applyNow, proratedAdjustmentInCents, chargeInCents, paymentDueInCents, creditAppliedInCents, targetPriceInCents, null, null);

        try
        {
            await _subscriptionService.CommitPlanChangeAsync(subscriptionId, targetProductHandle, applyNow, shownPreview, userName);
            StatusMessage = applyNow
                ? $"Subscription {subscriptionId} moved to {targetProductHandle} now."
                : $"Subscription {subscriptionId} is scheduled to move to {targetProductHandle} at the next renewal.";
        }
        catch (PlanChangePreviewStaleException)
        {
            ErrorMessage = "The previewed amount changed before you confirmed; please preview the plan change again.";
        }
        catch (Exception ex) when (IsExpectedSubscriptionError(ex))
        {
            ErrorMessage = ex.Message;
        }

        await LoadAsync();
        return Page();
    }

    private string RequireUserName()
    {
        Guard.Against.NullOrEmpty(User?.Identity?.Name, nameof(User.Identity.Name));
        return User.Identity!.Name!;
    }

    private static bool IsExpectedSubscriptionError(Exception ex) =>
        ex is BillingProviderException or InvalidSubscriptionTransitionException or SubscriptionAccessDeniedException or BillingConfigurationException;

    private async Task LoadAsync()
    {
        var userName = RequireUserName();

        try
        {
            Subscriptions = await _subscriptionService.GetMySubscriptionsAsync(userName);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Failed to load subscriptions for {0}: {1}", userName, ex.Message);
            ErrorMessage ??= "Your subscriptions are temporarily unavailable. Please try again shortly.";
        }

        try
        {
            AvailablePlans = await _subscriptionService.ListPlansAsync();
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Failed to list plans: {0}", ex.Message);
        }
    }
}
