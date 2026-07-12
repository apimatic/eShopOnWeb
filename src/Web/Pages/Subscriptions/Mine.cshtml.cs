using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>UC2/UC3/UC4 — view and manage the signed-in customer's subscriptions (mirror OrderController.MyOrders).</summary>
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

    public IReadOnlyList<Subscription> Subscriptions { get; private set; } = new List<Subscription>();
    public IReadOnlyList<BillingPlan> Plans { get; private set; } = new List<BillingPlan>();
    public string? ErrorMessage { get; private set; }
    public string? StatusMessage { get; private set; }
    public PlanChangePreview? Preview { get; private set; }
    public int? PreviewSubscriptionId { get; private set; }
    public string? PreviewTargetProductHandle { get; private set; }
    public PlanChangeTiming PreviewTiming { get; private set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostRecordUsageAsync(int subscriptionId, double quantity, string? memo)
    {
        var userReference = CurrentUserReference();
        try
        {
            var usage = await _subscriptionService.RecordUsageAsync(userReference, subscriptionId, quantity, memo);
            StatusMessage = usage.PeriodToDateTotal is { } total
                ? $"Recorded {usage.Quantity} unit(s) of usage. Period-to-date total: {total}."
                : $"Recorded {usage.Quantity} unit(s) of usage. (Running total is temporarily unavailable.)";
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (SubscriptionNotFoundException)
        {
            ErrorMessage = "Subscription not found.";
        }
        catch (InvalidSubscriptionStateException ex)
        {
            ErrorMessage = $"Cannot record usage: subscription is currently {ex.CurrentState}.";
        }
        catch (BillingConfigurationException ex)
        {
            _logger.LogWarning("Usage metering misconfigured: {0}", ex.Message);
            ErrorMessage = "Usage metering is temporarily unavailable.";
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Record usage failed: {0}", ex.Message);
            ErrorMessage = "We could not record usage right now. Please try again.";
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostPreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, PlanChangeTiming timing)
    {
        var userReference = CurrentUserReference();
        try
        {
            Preview = await _subscriptionService.PreviewPlanChangeAsync(userReference, subscriptionId, targetProductHandle, timing);
            PreviewSubscriptionId = subscriptionId;
            PreviewTargetProductHandle = targetProductHandle;
            PreviewTiming = timing;
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (SubscriptionNotFoundException)
        {
            ErrorMessage = "Subscription not found.";
        }
        catch (InvalidSubscriptionStateException ex)
        {
            ErrorMessage = $"Cannot change plan: subscription is currently {ex.CurrentState}.";
        }
        catch (BillingConfigurationException ex)
        {
            _logger.LogWarning("Plan change preview misconfigured: {0}", ex.Message);
            ErrorMessage = "This plan is temporarily unavailable.";
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Preview plan change failed: {0}", ex.Message);
            ErrorMessage = "We could not preview this plan change right now. Please try again.";
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCommitPlanChangeAsync(
        int subscriptionId,
        string targetProductHandle,
        PlanChangeTiming timing,
        int expectedProratedAdjustmentInCents,
        int expectedChargeInCents)
    {
        var userReference = CurrentUserReference();
        try
        {
            await _subscriptionService.CommitPlanChangeAsync(
                userReference, subscriptionId, targetProductHandle, timing,
                expectedProratedAdjustmentInCents, expectedChargeInCents);
            StatusMessage = "Plan change applied.";
        }
        catch (StalePlanChangePreviewException ex)
        {
            ErrorMessage = "The price changed since you last previewed this plan change. Please review the updated amount and confirm again.";
            Preview = ex.FreshPreview;
            PreviewSubscriptionId = subscriptionId;
            PreviewTargetProductHandle = targetProductHandle;
            PreviewTiming = timing;
        }
        catch (SubscriptionNotFoundException)
        {
            ErrorMessage = "Subscription not found.";
        }
        catch (InvalidSubscriptionStateException ex)
        {
            ErrorMessage = $"Cannot change plan: subscription is currently {ex.CurrentState}.";
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Commit plan change failed: {0}", ex.Message);
            ErrorMessage = "We could not apply this plan change right now. Please try again.";
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostLifecycleAsync(int subscriptionId, SubscriptionLifecycleAction action, string? reason)
    {
        var userReference = CurrentUserReference();
        try
        {
            var updated = await _subscriptionService.ApplyLifecycleActionAsync(userReference, subscriptionId, action, reason);
            StatusMessage = $"Subscription is now {updated.State}.";
        }
        catch (SubscriptionNotFoundException)
        {
            ErrorMessage = "Subscription not found.";
        }
        catch (InvalidSubscriptionStateException ex)
        {
            ErrorMessage = $"Cannot apply '{ex.RequestedAction}': subscription is currently {ex.CurrentState}.";
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Lifecycle action failed: {0}", ex.Message);
            ErrorMessage = "We could not apply that change right now. Please try again.";
        }

        await LoadAsync();
        return Page();
    }

    public static bool CanPause(Subscription s) => s.State is SubscriptionState.Active or SubscriptionState.Trialing or SubscriptionState.PastDue or SubscriptionState.Assessing or SubscriptionState.Suspended or SubscriptionState.AwaitingSignup;
    public static bool CanResume(Subscription s) => s.State == SubscriptionState.OnHold;
    public static bool CanCancel(Subscription s) => s.State is not (SubscriptionState.Canceled or SubscriptionState.Expired or SubscriptionState.FailedToCreate);
    public static bool CanReactivate(Subscription s) => s.State is SubscriptionState.Canceled or SubscriptionState.Expired;
    public static bool CanChangePlan(Subscription s) => s.State is not (SubscriptionState.Canceled or SubscriptionState.Expired or SubscriptionState.FailedToCreate or SubscriptionState.OnHold);
    public static bool CanRecordUsage(Subscription s) => s.State is SubscriptionState.Active or SubscriptionState.Trialing;

    private string CurrentUserReference()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        return User.Identity!.Name!;
    }

    private async Task LoadAsync()
    {
        var userReference = CurrentUserReference();
        try
        {
            Subscriptions = await _subscriptionService.GetMySubscriptionsAsync(userReference);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Failed to load subscriptions for {0}: {1}", userReference, ex.Message);
            Subscriptions = new List<Subscription>();
            ErrorMessage ??= "Your subscriptions are temporarily unavailable. Please try again later.";
        }

        try
        {
            Plans = await _subscriptionService.ListPlansAsync();
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Failed to load plans: {0}", ex.Message);
            Plans = new List<BillingPlan>();
        }
    }
}
