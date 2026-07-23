using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// UC2–UC4 — the customer's own management surface: review subscriptions, report metered usage, preview
/// and commit a plan change, and drive the lifecycle. Every operation is scoped to the signed-in user, so
/// a customer can never act on somebody else's subscription (plan.md §2.4).
/// </summary>
[Authorize]
public class MineModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly ISubscriptionSettings _settings;

    public MineModel(ISubscriptionService subscriptionService, ISubscriptionSettings settings)
    {
        _subscriptionService = subscriptionService;
        _settings = settings;
    }

    public IReadOnlyList<Subscription> Subscriptions { get; private set; } = Array.Empty<Subscription>();

    public IReadOnlyList<BillingPlan> Plans { get; private set; } = Array.Empty<BillingPlan>();

    /// <summary>Period-to-date usage, keyed by subscription id.</summary>
    public IReadOnlyDictionary<int, UsageSummary> Usage { get; private set; } =
        new Dictionary<int, UsageSummary>();

    /// <summary>A prorated quote awaiting the customer's confirmation, when one has been requested.</summary>
    public PlanChangePreview? Preview { get; private set; }

    public int? HighlightSubscriptionId { get; private set; }

    public string? StatusMessage { get; private set; }

    public string? ErrorMessage { get; private set; }

    public string MeteredComponentHandle => _settings.MeteredComponentHandle;

    public async Task OnGetAsync(int? highlight, CancellationToken cancellationToken)
    {
        HighlightSubscriptionId = highlight;
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostRecordUsageAsync(int subscriptionId, int quantity, string? memo,
        CancellationToken cancellationToken)
    {
        return await RunAsync(cancellationToken, async () =>
        {
            var summary = await _subscriptionService.RecordUsageAsync(
                subscriptionId, quantity, memo, UserReference(), cancellationToken);

            var total = summary.PeriodToDateQuantity.HasValue
                ? $"Period-to-date total is now {summary.PeriodToDateQuantity.Value} unit(s)" +
                  (summary.PeriodToDateCharge.HasValue
                      ? $" ({BillingMoney.ToDisplay(summary.PeriodToDateCharge.Value)})."
                      : ".")
                : "The running total is temporarily unavailable, but the usage was recorded.";

            StatusMessage = $"Recorded {quantity} unit(s) of '{summary.ComponentHandle}'. {total} " +
                            "It will appear on your next renewal invoice.";
        });
    }

    public async Task<IActionResult> OnPostPreviewAsync(int subscriptionId, string targetPlanHandle,
        PlanChangeTiming timing, CancellationToken cancellationToken)
    {
        return await RunAsync(cancellationToken, async () =>
        {
            Preview = await _subscriptionService.PreviewPlanChangeAsync(
                subscriptionId, targetPlanHandle, timing, UserReference(), cancellationToken);
        });
    }

    public async Task<IActionResult> OnPostChangePlanAsync(int subscriptionId, string targetPlanHandle,
        PlanChangeTiming timing, decimal previewedNetAmount, CancellationToken cancellationToken)
    {
        return await RunAsync(cancellationToken, async () =>
        {
            var updated = await _subscriptionService.ChangePlanAsync(
                subscriptionId, targetPlanHandle, timing, previewedNetAmount, UserReference(), cancellationToken);

            StatusMessage = timing == PlanChangeTiming.Immediately
                ? $"Your plan is now '{updated.PlanHandle}'. The prorated amount of " +
                  $"{BillingMoney.ToSignedDisplay(previewedNetAmount)} has been applied."
                : $"Your plan will change to '{targetPlanHandle}' at the start of the next billing period.";
        });
    }

    public async Task<IActionResult> OnPostLifecycleAsync(int subscriptionId, SubscriptionLifecycleAction action,
        string? reason, CancellationToken cancellationToken)
    {
        return await RunAsync(cancellationToken, async () =>
        {
            var updated = await _subscriptionService.ApplyLifecycleActionAsync(
                subscriptionId, action, reason, UserReference(), cancellationToken);

            var effective = action == SubscriptionLifecycleAction.CancelAtEndOfPeriod
                ? updated.CancellationScheduledAt ?? updated.CurrentPeriodEnd
                : null;

            StatusMessage = effective.HasValue
                ? $"Subscription {subscriptionId} is now '{updated.ProviderState}', effective {effective.Value:d MMM yyyy}."
                : $"Subscription {subscriptionId} is now '{updated.ProviderState}'.";
        });
    }

    /// <summary>
    /// Runs an action, then always reloads the page's data from the provider so the customer sees the
    /// provider's own view — which is the source of truth even when a transition was rejected (plan.md UC4).
    /// </summary>
    private async Task<IActionResult> RunAsync(CancellationToken cancellationToken, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException
                                       or InvalidSubscriptionOperationException)
        {
            ErrorMessage = ex.Message;
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var reference = UserReference();

        try
        {
            Subscriptions = await _subscriptionService.ListSubscriptionsAsync(reference, cancellationToken);
            Plans = await _subscriptionService.ListPlansAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException)
        {
            Subscriptions = Array.Empty<Subscription>();
            Plans = Array.Empty<BillingPlan>();
            ErrorMessage ??= "Your subscriptions are unavailable right now. Please try again shortly.";
            return;
        }

        var usage = new Dictionary<int, UsageSummary>();

        foreach (var subscription in Subscriptions.Where(s => s.CanRecordUsage))
        {
            try
            {
                var summary = await _subscriptionService.GetUsageSummaryAsync(subscription.Id, reference, cancellationToken);
                if (summary is not null)
                {
                    usage[subscription.Id] = summary;
                }
            }
            catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException
                                           or InvalidSubscriptionOperationException)
            {
                // A usage panel that cannot be read must not hide the subscription itself.
            }
        }

        Usage = usage;
    }

    private string UserReference()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        return User.Identity!.Name!;
    }
}
