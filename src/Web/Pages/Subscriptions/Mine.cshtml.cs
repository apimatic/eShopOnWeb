using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// The customer's own subscription management surface: usage reporting (UC2), plan changes with a
/// proration preview (UC3), and the lifecycle actions (UC4).
/// </summary>
/// <remarks>
/// Every action passes the signed-in username as the acting scope, so this page can only ever
/// reach the caller's own subscriptions. Cross-user administration lives in the PublicApi.
/// </remarks>
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

    public IReadOnlyList<Subscription> Subscriptions { get; private set; } = Array.Empty<Subscription>();

    public IReadOnlyList<BillingPlan> Plans { get; private set; } = Array.Empty<BillingPlan>();

    /// <summary>Set after a preview, so the page can show the cost and ask for confirmation.</summary>
    public PlanChangePreview? Preview { get; private set; }

    /// <summary>Set after usage has been recorded, so the running total can be shown.</summary>
    public UsageReport? LastUsage { get; private set; }

    public string? StatusMessage { get; private set; }

    public string? ErrorMessage { get; private set; }

    private string UserReference => User.Identity!.Name!;

    public async Task OnGet(CancellationToken cancellationToken)
    {
        StatusMessage = TempData["SubscriptionMessage"] as string;
        await LoadAsync(cancellationToken);
    }

    public Task<IActionResult> OnPostRecordUsage(int subscriptionId,
        int quantity,
        string? memo,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () =>
        {
            LastUsage = await _subscriptionService.RecordUsageAsync(
                subscriptionId, quantity, memo, UserReference, cancellationToken);

            StatusMessage = LastUsage.IsTotalAvailable
                ? $"Recorded {LastUsage.Recorded.Quantity} unit(s). " +
                  $"{LastUsage.PeriodToDateQuantity} unit(s) this period, " +
                  $"$ {LastUsage.PeriodToDateCharge:N2} — this will appear on your next renewal invoice."
                : $"Recorded {LastUsage.Recorded.Quantity} unit(s). {LastUsage.TotalUnavailableReason} " +
                  "The usage was accepted and will appear on your next renewal invoice.";
        }, cancellationToken);
    }

    public Task<IActionResult> OnPostPreviewPlanChange(int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () =>
        {
            Preview = await _subscriptionService.PreviewPlanChangeAsync(
                subscriptionId, targetPlanHandle, timing, UserReference, cancellationToken);
        }, cancellationToken);
    }

    public Task<IActionResult> OnPostChangePlan(int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        string confirmedFingerprint,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () =>
        {
            var previousPlanName = Subscriptions.FirstOrDefault(s => s.Id == subscriptionId)?.Plan.Name;

            var subscription = await _subscriptionService.ChangePlanAsync(
                subscriptionId, targetPlanHandle, timing, confirmedFingerprint, UserReference, cancellationToken);

            var effective = timing == PlanChangeTiming.AtNextRenewal
                ? subscription.CurrentPeriodEndsAt?.ToString("D") ?? "the next renewal"
                : "now";

            StatusMessage = previousPlanName is null
                ? $"Your plan is now {subscription.Plan.Name}, effective {effective}."
                : $"Your plan changed from {previousPlanName} to {subscription.Plan.Name}, effective {effective}.";
        }, cancellationToken);
    }

    public Task<IActionResult> OnPostLifecycle(int subscriptionId,
        string action,
        CancellationTiming timing,
        string? reason,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () =>
        {
            var subscription = action?.ToLowerInvariant() switch
            {
                "pause" => await _subscriptionService.PauseAsync(subscriptionId, UserReference, cancellationToken),
                "resume" => await _subscriptionService.ResumeAsync(subscriptionId, UserReference, cancellationToken),
                "cancel" => await _subscriptionService.CancelAsync(
                    subscriptionId, timing, reason, UserReference, cancellationToken),
                "reactivate" => await _subscriptionService.ReactivateAsync(
                    subscriptionId, UserReference, cancellationToken),
                _ => throw new ArgumentException($"'{action}' is not a supported lifecycle action.", nameof(action))
            };

            var effective = subscription.CancelAtEndOfPeriod && subscription.DelayedCancelAt.HasValue
                ? $" It will end on {subscription.DelayedCancelAt.Value:D}."
                : string.Empty;

            StatusMessage = $"Your subscription is now {subscription.State}.{effective}";
        }, cancellationToken);
    }

    /// <summary>
    /// Runs an action, turning every expected failure into a message on the page rather than an
    /// error screen, then reloads so the customer always sees the provider's current truth.
    /// </summary>
    private async Task<IActionResult> ExecuteAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        try
        {
            await action();
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (SubscriptionNotFoundException)
        {
            ErrorMessage = "That subscription could not be found on your account.";
        }
        catch (InvalidSubscriptionTransitionException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (StalePlanChangePreviewException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (BillingConfigurationException ex)
        {
            _logger.LogWarning("Subscription action failed because of a configuration problem: {0}", ex.Message);
            ErrorMessage = "Subscriptions are temporarily unavailable. Please try again later.";
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Subscription action failed for {0}: {1}", UserReference, ex.Message);
            ErrorMessage = ex.Message;
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            Subscriptions = await _subscriptionService.ListSubscriptionsForUserAsync(
                UserReference, cancellationToken);

            Plans = await _subscriptionService.ListPlansAsync(cancellationToken);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Could not load subscriptions for {0}: {1}", UserReference, ex.Message);
            ErrorMessage ??= "Your subscriptions are temporarily unavailable. Please try again shortly.";
        }
    }
}
