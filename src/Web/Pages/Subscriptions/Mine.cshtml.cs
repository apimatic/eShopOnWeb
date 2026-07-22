using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// UC2 / UC3 / UC4 — the customer's subscription management surface: record usage, preview and
/// commit a plan change, and run the lifecycle transitions.
/// </summary>
[Authorize]
public class MineModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;

    public MineModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public IReadOnlyList<BillingSubscription> Subscriptions { get; private set; } = Array.Empty<BillingSubscription>();

    public IReadOnlyList<BillingPlan> Plans { get; private set; } = Array.Empty<BillingPlan>();

    public UsageReport? LastUsageReport { get; private set; }

    public PlanChangePreview? LastPreview { get; private set; }

    public string? StatusMessage { get; private set; }

    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostRecordUsageAsync(int subscriptionId, decimal quantity, string? memo,
        CancellationToken cancellationToken)
    {
        return await RunAsync(async () =>
        {
            LastUsageReport = await _subscriptionService.RecordUsageAsync(Actor(), subscriptionId, quantity, memo,
                cancellationToken);

            StatusMessage = LastUsageReport.PeriodToDateAvailable
                ? $"Recorded {quantity} unit(s). Period to date: {LastUsageReport.PeriodToDateQuantity} unit(s), about $ {LastUsageReport.EstimatedPeriodToDateAmount:N2} on the next invoice."
                : $"Recorded {quantity} unit(s). The running period-to-date total is currently unavailable.";
        }, cancellationToken);
    }

    public async Task<IActionResult> OnPostPreviewPlanChangeAsync(int subscriptionId, string planHandle, string timing,
        CancellationToken cancellationToken)
    {
        return await RunAsync(async () =>
        {
            var changeTiming = ParseTiming(timing);

            LastPreview = await _subscriptionService.PreviewPlanChangeAsync(Actor(), subscriptionId, planHandle,
                changeTiming, cancellationToken);

            StatusMessage = changeTiming == PlanChangeTiming.Immediate
                ? $"Moving to {LastPreview.TargetPlanHandle} now would charge $ {LastPreview.PaymentDue:N2} after a credit of $ {LastPreview.CreditApplied:N2}."
                : $"Moving to {LastPreview.TargetPlanHandle} at the next renewal costs nothing now; the new price is $ {LastPreview.TargetPlanPrice:N2}.";
        }, cancellationToken);
    }

    public async Task<IActionResult> OnPostChangePlanAsync(int subscriptionId, string planHandle, string timing,
        decimal? previewedPaymentDue, CancellationToken cancellationToken)
    {
        return await RunAsync(async () =>
        {
            var result = await _subscriptionService.ChangePlanAsync(Actor(), subscriptionId, planHandle,
                ParseTiming(timing), previewedPaymentDue, cancellationToken);

            StatusMessage = result.Timing == PlanChangeTiming.Immediate
                ? $"Moved from {result.PreviousPlanHandle} to {result.NewPlanHandle}; $ {result.AppliedPaymentDue:N2} applied."
                : $"{result.NewPlanHandle} takes effect on {result.EffectiveAt:d}.";
        }, cancellationToken);
    }

    public async Task<IActionResult> OnPostLifecycleAsync(int subscriptionId, string action, string? timing,
        string? reason, CancellationToken cancellationToken)
    {
        return await RunAsync(async () =>
        {
            var lifecycleAction = Enum.Parse<SubscriptionLifecycleAction>(action, ignoreCase: true);
            var cancellationTiming = string.Equals(timing, nameof(SubscriptionCancellationTiming.EndOfPeriod),
                StringComparison.OrdinalIgnoreCase)
                    ? SubscriptionCancellationTiming.EndOfPeriod
                    : SubscriptionCancellationTiming.Immediate;

            var result = await _subscriptionService.ApplyLifecycleActionAsync(Actor(), subscriptionId,
                lifecycleAction, cancellationTiming, reason, cancellationToken);

            StatusMessage = result.EffectiveAt.HasValue
                ? $"{result.Action}: {result.PreviousState} → {result.NewState}, effective {result.EffectiveAt:d}."
                : $"{result.Action}: {result.PreviousState} → {result.NewState}.";

            if (!string.IsNullOrEmpty(result.Message))
            {
                StatusMessage += " " + result.Message;
            }
        }, cancellationToken);
    }

    private static PlanChangeTiming ParseTiming(string? timing)
        => string.Equals(timing, nameof(PlanChangeTiming.NextRenewal), StringComparison.OrdinalIgnoreCase)
            ? PlanChangeTiming.NextRenewal
            : PlanChangeTiming.Immediate;

    private SubscriptionActor Actor() => new(User.Identity?.Name ?? string.Empty, User.IsInRole("Administrators"));

    /// <summary>
    /// Runs one management action and always re-renders the page with the provider's current view,
    /// so an out-of-band change at the provider shows up immediately.
    /// </summary>
    private async Task<IActionResult> RunAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException
            or InvalidBillingRequestException or InvalidSubscriptionOperationException
            or SubscriptionAccessDeniedException or ArgumentException)
        {
            ErrorMessage = ex.Message;
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            Subscriptions = await _subscriptionService.ListMySubscriptionsAsync(Actor(), cancellationToken);
            Plans = await _subscriptionService.ListPlansAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException)
        {
            ErrorMessage ??= "Your subscriptions are unavailable right now. Please try again shortly.";
        }
    }
}
