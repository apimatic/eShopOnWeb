using System.Text.Json;
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
    private readonly IAppLogger<MineModel> _logger;

    public MineModel(ISubscriptionService subscriptionService, IAppLogger<MineModel> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    public IReadOnlyList<Subscription> Subscriptions { get; set; } = Array.Empty<Subscription>();
    public IReadOnlyList<BillingPlan> Plans { get; set; } = Array.Empty<BillingPlan>();
    public PendingPlanChangeView? PendingPreview { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? PendingPreviewJson { get; set; }

    public async Task OnGet()
    {
        var buyerId = RequireBuyerId();

        if (!string.IsNullOrEmpty(PendingPreviewJson))
        {
            PendingPreview = JsonSerializer.Deserialize<PendingPlanChangeView>(PendingPreviewJson);
        }

        try
        {
            Subscriptions = await _subscriptionService.GetSubscriptionsForBuyerAsync(buyerId);
            Plans = await _subscriptionService.ListPlansAsync();
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Failed to load subscriptions for {0}: {1}", buyerId, ex.Message);
            StatusMessage = "Your subscriptions are temporarily unavailable. Please try again later.";
        }
    }

    public async Task<IActionResult> OnPostPauseAsync(int subscriptionId)
    {
        var buyerId = RequireBuyerId();
        await RunLifecycleActionAsync(() => _subscriptionService.PauseSubscriptionAsync(subscriptionId, buyerId, isAdmin: false), "paused");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResumeAsync(int subscriptionId)
    {
        var buyerId = RequireBuyerId();
        await RunLifecycleActionAsync(() => _subscriptionService.ResumeSubscriptionAsync(subscriptionId, buyerId, isAdmin: false), "resumed");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCancelAsync(int subscriptionId, string cancellationTiming)
    {
        var buyerId = RequireBuyerId();
        var timing = string.Equals(cancellationTiming, "EndOfPeriod", StringComparison.OrdinalIgnoreCase)
            ? CancellationTiming.EndOfPeriod
            : CancellationTiming.Immediate;

        await RunLifecycleActionAsync(() => _subscriptionService.CancelSubscriptionAsync(subscriptionId, buyerId, isAdmin: false, timing, reason: null), "cancelled");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostReactivateAsync(int subscriptionId)
    {
        var buyerId = RequireBuyerId();
        await RunLifecycleActionAsync(() => _subscriptionService.ReactivateSubscriptionAsync(subscriptionId, buyerId, isAdmin: false), "reactivated");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRecordUsageAsync(int subscriptionId, int quantity, string? memo)
    {
        var buyerId = RequireBuyerId();

        try
        {
            var usage = await _subscriptionService.RecordUsageAsync(subscriptionId, buyerId, isAdmin: false, quantity, memo);
            StatusMessage = usage.PeriodToDateTotal.HasValue
                ? $"Recorded {usage.QuantityRecorded} unit(s) of usage. Period-to-date total: {usage.PeriodToDateTotal}."
                : $"Recorded {usage.QuantityRecorded} unit(s) of usage. Running total is temporarily unavailable.";
        }
        catch (Exception ex) when (ex is BillingProviderException or InvalidSubscriptionStateException or UnauthorizedSubscriptionAccessException)
        {
            _logger.LogWarning("Record usage failed for subscription {0}: {1}", subscriptionId, ex.Message);
            StatusMessage = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, string timing)
    {
        var buyerId = RequireBuyerId();
        var planChangeTiming = ParseTiming(timing);

        try
        {
            var preview = await _subscriptionService.PreviewPlanChangeAsync(subscriptionId, buyerId, isAdmin: false, targetProductHandle, planChangeTiming);
            PendingPreviewJson = JsonSerializer.Serialize(new PendingPlanChangeView
            {
                SubscriptionId = preview.SubscriptionId,
                TargetProductHandle = preview.TargetProductHandle,
                Timing = preview.Timing.ToString(),
                ComparableAmountInCents = preview.ComparableAmountInCents,
                ProratedAdjustmentInCents = preview.ProratedAdjustmentInCents,
                ChargeInCents = preview.ChargeInCents,
                CreditAppliedInCents = preview.CreditAppliedInCents,
                EffectiveAt = preview.EffectiveAt
            });
        }
        catch (Exception ex) when (ex is BillingProviderException or InvalidSubscriptionStateException or UnauthorizedSubscriptionAccessException or ArgumentException)
        {
            _logger.LogWarning("Plan-change preview failed for subscription {0}: {1}", subscriptionId, ex.Message);
            StatusMessage = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCommitPlanChangeAsync(int subscriptionId, string targetProductHandle, string timing, long previewedAmountInCents)
    {
        var buyerId = RequireBuyerId();
        var planChangeTiming = ParseTiming(timing);

        try
        {
            await _subscriptionService.CommitPlanChangeAsync(subscriptionId, buyerId, isAdmin: false, targetProductHandle, planChangeTiming, previewedAmountInCents);
            StatusMessage = "Plan change applied.";
        }
        catch (Exception ex) when (ex is BillingProviderException or InvalidSubscriptionStateException or UnauthorizedSubscriptionAccessException or StalePlanChangePreviewException)
        {
            _logger.LogWarning("Plan-change commit failed for subscription {0}: {1}", subscriptionId, ex.Message);
            StatusMessage = ex.Message;
        }

        return RedirectToPage();
    }

    private async Task RunLifecycleActionAsync(Func<Task<Subscription>> action, string pastTenseVerb)
    {
        try
        {
            var updated = await action();
            StatusMessage = $"Subscription {updated.Id} {pastTenseVerb}.";
        }
        catch (Exception ex) when (ex is BillingProviderException or InvalidSubscriptionStateException or UnauthorizedSubscriptionAccessException)
        {
            _logger.LogWarning("Lifecycle action failed: {0}", ex.Message);
            StatusMessage = ex.Message;
        }
    }

    private static PlanChangeTiming ParseTiming(string timing)
        => string.Equals(timing, "AtRenewal", StringComparison.OrdinalIgnoreCase) ? PlanChangeTiming.AtRenewal : PlanChangeTiming.Now;

    private string RequireBuyerId()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        return User.Identity!.Name!;
    }
}

public class PendingPlanChangeView
{
    public int SubscriptionId { get; set; }
    public string TargetProductHandle { get; set; } = string.Empty;
    public string Timing { get; set; } = string.Empty;
    public long ComparableAmountInCents { get; set; }
    public long? ProratedAdjustmentInCents { get; set; }
    public long? ChargeInCents { get; set; }
    public long? CreditAppliedInCents { get; set; }
    public DateTimeOffset EffectiveAt { get; set; }
}
