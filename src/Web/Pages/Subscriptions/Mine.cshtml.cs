using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// The customer's subscription management surface: view state, report pay-as-you-go usage (UC2),
/// preview and commit a plan change (UC3), and run the lifecycle actions (UC4).
/// </summary>
/// <remarks>
/// The signed-in customer comes from the cookie session (<c>User.Identity.Name</c>), which is also the
/// reference the billing provider knows them by, so a customer can only ever act on their own
/// subscription (plan.md §2.4/§4.4).
/// </remarks>
[Authorize]
public class MineModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;

    public MineModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public IReadOnlyList<Subscription> Subscriptions { get; private set; } = Array.Empty<Subscription>();

    public IReadOnlyList<SubscriptionPlan> Plans { get; private set; } = Array.Empty<SubscriptionPlan>();

    /// <summary>Period-to-date usage for the customer's active subscription, when they have one.</summary>
    public UsageReport? Usage { get; private set; }

    /// <summary>A proration preview awaiting confirmation (UC3, step 3).</summary>
    public PlanChangePreview? PendingPreview { get; private set; }

    /// <summary>The lifecycle actions each subscription may legally attempt.</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<SubscriptionLifecycleAction>> AllowedActions { get; private set; }
        = new Dictionary<int, IReadOnlyList<SubscriptionLifecycleAction>>();

    [TempData]
    public string? StatusMessage { get; set; }

    public string? ErrorMessage { get; private set; }

    private string UserName => User.Identity!.Name!;

    private SubscriptionActor Actor => SubscriptionActor.Customer(UserName);

    public Task OnGetAsync(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostRecordUsageAsync(
        int subscriptionId,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(cancellationToken, async () =>
        {
            var report = await _subscriptionService.RecordUsageAsync(
                Actor, subscriptionId, quantity, memo, cancellationToken);

            var total = report.PeriodToDateAvailable
                ? $"Period-to-date usage is now {report.PeriodToDateUnits:N0} unit(s)"
                : "The running total is unavailable right now";

            StatusMessage =
                $"Recorded {report.Record?.Quantity ?? quantity:N0} unit(s) of {report.ComponentHandle}. " +
                $"{total}; it will appear on your next renewal invoice.";
        });
    }

    public async Task<IActionResult> OnPostPreviewPlanChangeAsync(
        int subscriptionId,
        string targetPlanHandle,
        CancellationToken cancellationToken)
    {
        try
        {
            PendingPreview = await _subscriptionService.PreviewPlanChangeAsync(
                Actor, subscriptionId, targetPlanHandle, cancellationToken);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            ErrorMessage = ex.Message;
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostChangePlanAsync(
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        long? confirmedPaymentDueInCents,
        DateTimeOffset? previewedAt,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(cancellationToken, async () =>
        {
            var request = new PlanChangeRequest
            {
                TargetPlanHandle = targetPlanHandle,
                Timing = timing,
                ConfirmedPaymentDueInCents = confirmedPaymentDueInCents,
                PreviewedAt = previewedAt
            };

            var subscription = await _subscriptionService.ChangePlanAsync(
                Actor, subscriptionId, request, cancellationToken);

            StatusMessage = timing == PlanChangeTiming.Immediately
                ? $"Your subscription now runs on {subscription.PlanName ?? subscription.PlanHandle}."
                : $"Your subscription will move to {targetPlanHandle} at the next renewal" +
                  $"{FormatEffectiveDate(subscription.CurrentPeriodEndsAt)}.";
        });
    }

    public async Task<IActionResult> OnPostLifecycleAsync(
        int subscriptionId,
        SubscriptionLifecycleAction action,
        string? reason,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(cancellationToken, async () =>
        {
            var subscription = await _subscriptionService.ExecuteLifecycleActionAsync(
                Actor, subscriptionId, SubscriptionLifecycleRequest.For(action, reason), cancellationToken);

            StatusMessage = action == SubscriptionLifecycleAction.CancelAtEndOfPeriod
                ? $"Your subscription will be cancelled at the end of the current period" +
                  $"{FormatEffectiveDate(subscription.ScheduledCancellationAt ?? subscription.CurrentPeriodEndsAt)}."
                : $"Your subscription is now {subscription.State}.";
        });
    }

    /// <summary>
    /// Runs a state-changing action, redirecting on success so a refresh cannot replay it, and
    /// re-rendering with a friendly message on any expected domain or provider failure.
    /// </summary>
    private async Task<IActionResult> ExecuteAsync(CancellationToken cancellationToken, Func<Task> action)
    {
        try
        {
            await action();
            return RedirectToPage();
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            ErrorMessage = ex.Message;
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            Subscriptions = await _subscriptionService.ListSubscriptionsAsync(UserName, cancellationToken);
            Plans = await _subscriptionService.ListPlansAsync(cancellationToken);

            AllowedActions = Subscriptions.ToDictionary(
                subscription => subscription.Id,
                SubscriptionLifecyclePolicy.AllowedActions);

            var active = Subscriptions.FirstOrDefault(subscription => subscription.IsActive);
            if (active is not null)
            {
                Usage = await _subscriptionService.GetUsageSummaryAsync(Actor, active.Id, cancellationToken);
            }
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            ErrorMessage ??= $"Your subscriptions are unavailable right now. {ex.Message}";
        }
    }

    /// <summary>The domain and provider failures this page is expected to surface rather than throw.</summary>
    private static bool IsExpected(Exception exception) => exception is
        BillingProviderException or
        BillingConfigurationException or
        SubscriptionNotFoundException or
        SubscriptionAccessDeniedException or
        InvalidSubscriptionTransitionException or
        InvalidPlanChangeException or
        StalePlanChangePreviewException or
        InvalidUsageQuantityException or
        NoActiveSubscriptionException;

    private static string FormatEffectiveDate(DateTimeOffset? effectiveAt) =>
        effectiveAt is null ? string.Empty : $" on {effectiveAt.Value.LocalDateTime:d}";
}
