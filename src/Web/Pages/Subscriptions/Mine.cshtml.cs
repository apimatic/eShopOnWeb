using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.Subscriptions;
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

    public IReadOnlyList<CustomerSubscription> Subscriptions { get; set; } = Array.Empty<CustomerSubscription>();
    public IReadOnlyList<SubscriptionPlan> AvailablePlans { get; set; } = Array.Empty<SubscriptionPlan>();
    public string? InfoMessage { get; set; }
    public string? ErrorMessage { get; set; }

    // Populated only right after a plan-change preview is requested, so the confirm button
    // can echo the exact amount the customer was shown (staleness check on commit).
    public int? PreviewedSubscriptionId { get; set; }
    public string? PreviewedTargetPlanHandle { get; set; }
    public PlanChangePreview? Preview { get; set; }

    public async Task OnGetAsync()
    {
        InfoMessage = TempData["SubscriptionMessage"] as string;
        ErrorMessage = TempData["SubscriptionError"] as string;
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostRecordUsageAsync(int subscriptionId, double quantity)
    {
        var userName = RequireUserName();

        try
        {
            var result = await _subscriptionService.RecordUsageAsync(userName, subscriptionId, quantity, null);
            TempData["SubscriptionMessage"] = result.PeriodToDateBalance.HasValue
                ? $"Recorded {result.Quantity:0.##} unit(s) of usage on subscription #{subscriptionId}. Period-to-date balance: {result.PeriodToDateBalance} unit(s)."
                : $"Recorded {result.Quantity:0.##} unit(s) of usage on subscription #{subscriptionId}. (Running balance temporarily unavailable.)";
        }
        catch (Exception ex) when (IsSubscriptionDomainException(ex))
        {
            TempData["SubscriptionError"] = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle)
    {
        RequireUserName();
        await LoadAsync();

        try
        {
            Preview = await _subscriptionService.PreviewPlanChangeAsync(User!.Identity!.Name, subscriptionId,
                targetPlanHandle);
            PreviewedSubscriptionId = subscriptionId;
            PreviewedTargetPlanHandle = targetPlanHandle;
        }
        catch (Exception ex) when (IsSubscriptionDomainException(ex))
        {
            ErrorMessage = ex.Message;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostCommitPlanChangeAsync(int subscriptionId, string targetPlanHandle,
        string timing, long? expectedProratedAdjustmentInCents)
    {
        var userName = RequireUserName();
        var parsedTiming = string.Equals(timing, nameof(PlanChangeTiming.AtNextRenewal), StringComparison.Ordinal)
            ? PlanChangeTiming.AtNextRenewal
            : PlanChangeTiming.Now;

        try
        {
            await _subscriptionService.CommitPlanChangeAsync(userName, subscriptionId, targetPlanHandle,
                parsedTiming, expectedProratedAdjustmentInCents);
            TempData["SubscriptionMessage"] = parsedTiming == PlanChangeTiming.Now
                ? $"Subscription #{subscriptionId} moved to {targetPlanHandle} now."
                : $"Subscription #{subscriptionId} will move to {targetPlanHandle} at the next renewal.";
        }
        catch (Exception ex) when (IsSubscriptionDomainException(ex))
        {
            TempData["SubscriptionError"] = ex.Message;
        }

        return RedirectToPage();
    }

    public Task<IActionResult> OnPostPauseAsync(int subscriptionId) =>
        RunLifecycleActionAsync(subscriptionId, "paused",
            userName => _subscriptionService.PauseAsync(userName, subscriptionId));

    public Task<IActionResult> OnPostResumeAsync(int subscriptionId) =>
        RunLifecycleActionAsync(subscriptionId, "resumed",
            userName => _subscriptionService.ResumeAsync(userName, subscriptionId));

    public Task<IActionResult> OnPostReactivateAsync(int subscriptionId) =>
        RunLifecycleActionAsync(subscriptionId, "reactivated",
            userName => _subscriptionService.ReactivateAsync(userName, subscriptionId));

    public Task<IActionResult> OnPostCancelAsync(int subscriptionId, bool endOfPeriod, string? reason) =>
        RunLifecycleActionAsync(subscriptionId, endOfPeriod ? "scheduled for cancellation at period end" : "cancelled",
            userName => _subscriptionService.CancelAsync(userName, subscriptionId, reason, endOfPeriod));

    private async Task<IActionResult> RunLifecycleActionAsync(int subscriptionId, string verb,
        Func<string, Task<CustomerSubscription>> action)
    {
        var userName = RequireUserName();

        try
        {
            await action(userName);
            TempData["SubscriptionMessage"] = $"Subscription #{subscriptionId} {verb}.";
        }
        catch (Exception ex) when (IsSubscriptionDomainException(ex))
        {
            TempData["SubscriptionError"] = ex.Message;
        }

        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        var userName = RequireUserName();
        Subscriptions = await _subscriptionService.GetMySubscriptionsAsync(userName);
        AvailablePlans = await _subscriptionService.GetAvailablePlansAsync();
    }

    private string RequireUserName()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        return User.Identity.Name;
    }

    private static bool IsSubscriptionDomainException(Exception ex) =>
        ex is InvalidSubscriptionRequestException or SubscriptionConflictException or SubscriptionNotFoundException
            or BillingProviderException;
}
