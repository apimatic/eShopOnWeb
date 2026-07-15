using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>UC2/UC3/UC4 management surface — [Authorize], mirrors OrderController.MyOrders' "view/manage own" pattern.</summary>
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

    public IReadOnlyList<Subscription> UserSubscriptions { get; set; } = Array.Empty<Subscription>();
    public IReadOnlyList<BillingPlan> Plans { get; set; } = Array.Empty<BillingPlan>();
    public string? StatusMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public PlanChangePreview? PendingPreview { get; set; }

    public async Task OnGet()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostRecordUsageAsync(int subscriptionId, int quantity)
    {
        var userId = RequireUserId();

        try
        {
            var usage = await _subscriptionService.RecordUsageAsync(subscriptionId, userId, quantity, "Recorded from My Subscriptions page");
            StatusMessage = usage.TotalAvailable
                ? $"Recorded {usage.RecordedQuantity} unit(s) of usage. Period-to-date total: {usage.PeriodToDateTotal}."
                : $"Recorded {usage.RecordedQuantity} unit(s) of usage. Period-to-date total is temporarily unavailable.";
        }
        catch (Exception ex) when (IsBillingFailure(ex))
        {
            ErrorMessage = FriendlyMessage(ex);
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostPreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, string timing)
    {
        var userId = RequireUserId();

        try
        {
            var timingValue = Enum.Parse<PlanChangeTiming>(timing);
            PendingPreview = await _subscriptionService.PreviewPlanChangeAsync(subscriptionId, userId, targetProductHandle, timingValue);
        }
        catch (Exception ex) when (IsBillingFailure(ex))
        {
            ErrorMessage = FriendlyMessage(ex);
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCommitPlanChangeAsync(
        int subscriptionId,
        string currentProductHandle,
        string targetProductHandle,
        string timing,
        long? proratedAdjustmentInCents,
        long? chargeInCents,
        long? paymentDueInCents,
        long? creditAppliedInCents,
        long newPlanPriceInCents,
        DateTimeOffset? effectiveAt)
    {
        var userId = RequireUserId();

        try
        {
            var timingValue = Enum.Parse<PlanChangeTiming>(timing);
            var confirmedPreview = new PlanChangePreview(
                subscriptionId, currentProductHandle, targetProductHandle, timingValue,
                proratedAdjustmentInCents, chargeInCents, paymentDueInCents, creditAppliedInCents,
                newPlanPriceInCents, effectiveAt);

            await _subscriptionService.CommitPlanChangeAsync(subscriptionId, userId, confirmedPreview);
            StatusMessage = "Plan change applied.";
        }
        catch (Exception ex) when (IsBillingFailure(ex))
        {
            ErrorMessage = FriendlyMessage(ex);
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostPauseAsync(int subscriptionId)
    {
        var userId = RequireUserId();
        return await ApplyTransitionAsync(() => _subscriptionService.PauseAsync(subscriptionId, userId));
    }

    public async Task<IActionResult> OnPostResumeAsync(int subscriptionId)
    {
        var userId = RequireUserId();
        return await ApplyTransitionAsync(() => _subscriptionService.ResumeAsync(subscriptionId, userId));
    }

    public async Task<IActionResult> OnPostReactivateAsync(int subscriptionId)
    {
        var userId = RequireUserId();
        return await ApplyTransitionAsync(() => _subscriptionService.ReactivateAsync(subscriptionId, userId));
    }

    public async Task<IActionResult> OnPostCancelAsync(int subscriptionId, string timing, string? reason)
    {
        var userId = RequireUserId();
        var timingValue = Enum.Parse<CancellationTiming>(timing);
        return await ApplyTransitionAsync(() => _subscriptionService.CancelAsync(subscriptionId, userId, timingValue, reason));
    }

    private async Task<IActionResult> ApplyTransitionAsync(Func<Task<Subscription>> transition)
    {
        try
        {
            await transition();
            StatusMessage = "Subscription updated.";
        }
        catch (Exception ex) when (IsBillingFailure(ex))
        {
            ErrorMessage = FriendlyMessage(ex);
        }

        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        var userId = RequireUserId();

        try
        {
            UserSubscriptions = await _subscriptionService.GetSubscriptionsForUserAsync(userId);
            Plans = await _subscriptionService.ListPlansAsync();
        }
        catch (Exception ex) when (IsBillingFailure(ex))
        {
            ErrorMessage ??= FriendlyMessage(ex);
        }
    }

    private string RequireUserId()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        return User.Identity!.Name!;
    }

    private static bool IsBillingFailure(Exception ex) =>
        ex is BillingConfigurationException or BillingProviderException or InvalidSubscriptionStateException
            or StalePlanChangePreviewException or SubscriptionNotFoundException or ArgumentException;

    private string FriendlyMessage(Exception ex)
    {
        _logger.LogWarning("Subscription action failed: {Message}", ex.Message);
        return ex.Message;
    }
}
