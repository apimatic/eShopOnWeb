using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// UC2/UC3/UC4: view and manage the signed-in customer's own subscriptions (mirrors
/// OrderController.MyOrders) — record usage, preview/commit a plan change, and drive lifecycle actions.
/// </summary>
[Authorize]
public class MineModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly MaxioSettings _maxioSettings;
    private readonly IAppLogger<MineModel> _logger;

    public MineModel(ISubscriptionService subscriptionService, IOptions<MaxioSettings> maxioSettings, IAppLogger<MineModel> logger)
    {
        _subscriptionService = subscriptionService;
        _maxioSettings = maxioSettings.Value;
        _logger = logger;
    }

    public IReadOnlyList<SubscriptionViewModel> Subscriptions { get; set; } = new List<SubscriptionViewModel>();
    public string? ErrorMessage { get; set; }
    public string? StatusMessage { get; set; }
    public PlanChangePreviewViewModel? Preview { get; set; }
    public int? UsageSubscriptionId { get; set; }
    public int? UsagePeriodToDateUnits { get; set; }
    public bool UsagePeriodToDateAvailable { get; set; }

    public async Task OnGet()
    {
        await LoadSubscriptionsAsync();
    }

    public async Task<IActionResult> OnPostRecordUsageAsync(int subscriptionId, double quantity, string? memo)
    {
        var userId = RequireUserId();
        try
        {
            var reading = await _subscriptionService.RecordUsageAsync(userId, actingAsAdmin: false, subscriptionId, quantity, memo);
            UsageSubscriptionId = subscriptionId;
            UsagePeriodToDateUnits = reading.PeriodToDateUnits;
            UsagePeriodToDateAvailable = reading.PeriodToDateAvailable;
            StatusMessage = reading.PeriodToDateAvailable
                ? $"Recorded {quantity} unit(s). Period-to-date total: {reading.PeriodToDateUnits}."
                : $"Recorded {quantity} unit(s). Period-to-date total is temporarily unavailable.";
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (SubscriptionNotFoundException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (InvalidSubscriptionTransitionException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (BillingConfigurationException ex)
        {
            _logger.LogWarning("Usage recording failed (configuration): {0}", ex.Message);
            ErrorMessage = "Usage recording is temporarily unavailable. Please contact support.";
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Usage recording failed (provider): {0}", ex.Message);
            ErrorMessage = "We couldn't record usage right now. Please try again shortly.";
        }

        await LoadSubscriptionsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostPreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, bool applyImmediately)
    {
        var userId = RequireUserId();
        try
        {
            var preview = await _subscriptionService.PreviewPlanChangeAsync(userId, subscriptionId, targetProductHandle, applyImmediately);
            Preview = new PlanChangePreviewViewModel
            {
                SubscriptionId = subscriptionId,
                TargetProductHandle = preview.TargetProductHandle,
                ApplyImmediately = preview.ApplyImmediately,
                ProratedAdjustmentInCents = preview.ProratedAdjustmentInCents,
                ChargeInCents = preview.ChargeInCents,
                PaymentDueInCents = preview.PaymentDueInCents,
                CreditAppliedInCents = preview.CreditAppliedInCents,
                StalenessToken = preview.StalenessToken
            };
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (SubscriptionNotFoundException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (InvalidSubscriptionTransitionException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (BillingConfigurationException ex)
        {
            _logger.LogWarning("Plan change preview failed (configuration): {0}", ex.Message);
            ErrorMessage = "That plan is temporarily unavailable. Please try again later.";
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Plan change preview failed (provider): {0}", ex.Message);
            ErrorMessage = "We couldn't preview that plan change right now. Please try again shortly.";
        }

        await LoadSubscriptionsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCommitPlanChangeAsync(int subscriptionId, string targetProductHandle, bool applyImmediately, string stalenessToken)
    {
        var userId = RequireUserId();
        try
        {
            await _subscriptionService.CommitPlanChangeAsync(userId, subscriptionId, targetProductHandle, applyImmediately, stalenessToken);
            StatusMessage = $"Plan changed to '{targetProductHandle}'.";
        }
        catch (PlanChangePreviewStaleException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (SubscriptionNotFoundException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (InvalidSubscriptionTransitionException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (BillingConfigurationException ex)
        {
            _logger.LogWarning("Plan change commit failed (configuration): {0}", ex.Message);
            ErrorMessage = "That plan is temporarily unavailable. Please try again later.";
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Plan change commit failed (provider): {0}", ex.Message);
            ErrorMessage = "We couldn't apply that plan change right now. Please try again shortly.";
        }

        await LoadSubscriptionsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostPauseAsync(int subscriptionId) =>
        await RunLifecycleActionAsync(subscriptionId, () => _subscriptionService.PauseAsync(RequireUserId(), actingAsAdmin: false, subscriptionId), "paused");

    public async Task<IActionResult> OnPostResumeAsync(int subscriptionId) =>
        await RunLifecycleActionAsync(subscriptionId, () => _subscriptionService.ResumeAsync(RequireUserId(), actingAsAdmin: false, subscriptionId), "resumed");

    public async Task<IActionResult> OnPostCancelAsync(int subscriptionId, bool endOfPeriod, string? reason) =>
        await RunLifecycleActionAsync(subscriptionId, () => _subscriptionService.CancelAsync(RequireUserId(), actingAsAdmin: false, subscriptionId, endOfPeriod, reason),
            endOfPeriod ? "scheduled to cancel at the end of the current period" : "canceled");

    public async Task<IActionResult> OnPostReactivateAsync(int subscriptionId) =>
        await RunLifecycleActionAsync(subscriptionId, () => _subscriptionService.ReactivateAsync(RequireUserId(), actingAsAdmin: false, subscriptionId), "reactivated");

    private async Task<IActionResult> RunLifecycleActionAsync(int subscriptionId, Func<Task<ApplicationCore.Entities.SubscriptionAggregate.Subscription>> action, string successVerb)
    {
        try
        {
            await action();
            StatusMessage = $"Subscription {subscriptionId} was {successVerb}.";
        }
        catch (SubscriptionNotFoundException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (InvalidSubscriptionTransitionException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Lifecycle action failed (provider): {0}", ex.Message);
            ErrorMessage = "We couldn't complete that action right now. Please try again shortly.";
        }

        await LoadSubscriptionsAsync();
        return Page();
    }

    private async Task LoadSubscriptionsAsync()
    {
        try
        {
            var subscriptions = await _subscriptionService.GetSubscriptionsForUserAsync(RequireUserId());
            Subscriptions = subscriptions.Select(s => new SubscriptionViewModel
            {
                SubscriptionId = s.Id,
                ProductHandle = s.ProductHandle,
                State = s.State,
                CancelAtEndOfPeriod = s.CancelAtEndOfPeriod,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                OtherPlanHandle = string.Equals(s.ProductHandle, _maxioSettings.DefaultProductHandle, StringComparison.OrdinalIgnoreCase)
                    ? _maxioSettings.AlternateProductHandle
                    : _maxioSettings.DefaultProductHandle
            }).ToList();
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Could not list subscriptions: {0}", ex.Message);
            ErrorMessage = "Your subscriptions are temporarily unavailable. Please try again shortly.";
        }
    }

    private string RequireUserId()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        return User!.Identity!.Name!;
    }
}
