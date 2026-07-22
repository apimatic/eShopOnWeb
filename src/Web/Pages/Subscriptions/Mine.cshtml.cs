using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// The customer's own subscription management surface: current state, the usage panel (UC2), plan change
/// with a proration preview (UC3) and the four lifecycle actions (UC4).
/// </summary>
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

    public IReadOnlyCollection<CustomerSubscription> Subscriptions { get; private set; } = Array.Empty<CustomerSubscription>();

    public IReadOnlyCollection<SubscriptionPlan> Plans { get; private set; } = Array.Empty<SubscriptionPlan>();

    public UsageSummary? Usage { get; private set; }

    /// <summary>Set when the customer has asked to see what a plan change would cost, before confirming.</summary>
    public PlanChangePreview? Preview { get; private set; }

    public int? HighlightSubscriptionId { get; private set; }

    public string? ErrorMessage { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGet(int? highlight, CancellationToken cancellationToken)
    {
        HighlightSubscriptionId = highlight;
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostRecordUsage(int subscriptionId, decimal quantity, string? memo, CancellationToken cancellationToken)
    {
        var userName = RequireUserName();

        try
        {
            var report = await _subscriptionService.RecordUsageAsync(subscriptionId, quantity, memo, userName, cancellationToken);

            StatusMessage = report.TotalsAvailable
                ? $"Recorded {report.Record.Quantity:N0} unit(s). This period's usage is now {report.PeriodToDateQuantity:N0} unit(s), which will add ${report.PeriodToDateCharge:N2} to your next renewal invoice."
                : $"Recorded {report.Record.Quantity:N0} unit(s). It will appear on your next renewal invoice; the running total is temporarily unavailable.";

            return RedirectToPage(new { highlight = subscriptionId });
        }
        catch (Exception ex) when (ex is InvalidSubscriptionOperationException or BillingConfigurationException or BillingProviderException)
        {
            return await FailAsync(ex, "Usage could not be recorded", cancellationToken);
        }
    }

    public async Task<IActionResult> OnPostPreviewPlanChange(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing, CancellationToken cancellationToken)
    {
        var userName = RequireUserName();

        try
        {
            await LoadAsync(cancellationToken);
            Preview = await _subscriptionService.PreviewPlanChangeAsync(subscriptionId, targetPlanHandle, timing, userName, cancellationToken);

            return Page();
        }
        catch (Exception ex) when (ex is InvalidSubscriptionOperationException or BillingConfigurationException or BillingProviderException)
        {
            return await FailAsync(ex, "The plan change could not be previewed", cancellationToken);
        }
    }

    public async Task<IActionResult> OnPostChangePlan(int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        decimal confirmedAmountDue,
        CancellationToken cancellationToken)
    {
        var userName = RequireUserName();

        try
        {
            var result = await _subscriptionService.ChangePlanAsync(subscriptionId, targetPlanHandle, timing, confirmedAmountDue, userName, cancellationToken);

            var effective = result.Timing == PlanChangeTiming.Immediately
                ? "immediately"
                : "at your next renewal";

            StatusMessage =
                $"Your plan changed from {result.PreviousPlanName ?? result.PreviousPlanHandle ?? "your previous plan"} " +
                $"to {result.TargetPlanName} {effective}. Amount applied: ${result.AmountApplied:N2}.";

            return RedirectToPage(new { highlight = subscriptionId });
        }
        catch (Exception ex) when (ex is InvalidSubscriptionOperationException or BillingConfigurationException or BillingProviderException)
        {
            return await FailAsync(ex, "The plan change could not be completed", cancellationToken);
        }
    }

    public async Task<IActionResult> OnPostLifecycle(int subscriptionId, SubscriptionLifecycleAction action, string? reason, CancellationToken cancellationToken)
    {
        var userName = RequireUserName();

        try
        {
            var updated = await _subscriptionService.ApplyLifecycleActionAsync(subscriptionId, action, reason, userName, cancellationToken);

            StatusMessage = updated.IsPendingCancellation && action == SubscriptionLifecycleAction.CancelAtPeriodEnd
                ? $"Your subscription will end on {updated.DelayedCancelAt?.ToString("d MMMM yyyy") ?? updated.CurrentPeriodEndsAt?.ToString("d MMMM yyyy") ?? "the end of the current period"}."
                : $"Your subscription is now {updated.Status}.";

            return RedirectToPage(new { highlight = subscriptionId });
        }
        catch (Exception ex) when (ex is InvalidSubscriptionOperationException or BillingConfigurationException or BillingProviderException)
        {
            return await FailAsync(ex, "That change could not be applied", cancellationToken);
        }
    }

    private async Task<IActionResult> FailAsync(Exception exception, string prefix, CancellationToken cancellationToken)
    {
        _logger.LogWarning("{Prefix} for {User}: {Reason}", prefix, User?.Identity?.Name ?? "(anonymous)", exception.Message);

        await LoadAsync(cancellationToken);

        ErrorMessage = exception is BillingProviderException provider
            ? $"{prefix}: {provider.ProviderMessage}"
            : $"{prefix}: {exception.Message}";

        return Page();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var userName = RequireUserName();

        try
        {
            Subscriptions = await _subscriptionService.GetSubscriptionsAsync(userName, cancellationToken);
            Plans = await _subscriptionService.GetPlansAsync(cancellationToken);

            var active = Subscriptions.FirstOrDefault(subscription => subscription.IsActive);
            if (active is not null)
            {
                Usage = await _subscriptionService.GetUsageSummaryAsync(active.Id, userName, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is BillingConfigurationException or BillingProviderException)
        {
            _logger.LogWarning("Subscriptions could not be loaded for {User}: {Reason}", userName, ex.Message);
            ErrorMessage = "Your subscription details are unavailable right now. Please try again shortly.";
        }
    }

    private string RequireUserName()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));

        return User.Identity.Name!;
    }
}
