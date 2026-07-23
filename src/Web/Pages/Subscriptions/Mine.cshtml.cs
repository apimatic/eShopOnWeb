using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// The customer's subscription management surface: usage reporting (UC2), plan change with a
/// proration preview (UC3), and the four lifecycle transitions (UC4).
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

    public IReadOnlyCollection<Subscription> Subscriptions { get; private set; } = Array.Empty<Subscription>();

    /// <summary>The plans available as a plan-change target.</summary>
    public IReadOnlyCollection<BillingPlan> Plans { get; private set; } = Array.Empty<BillingPlan>();

    /// <summary>A subscription id to draw attention to, set after a successful subscribe.</summary>
    [BindProperty(SupportsGet = true)]
    public int? Highlight { get; set; }

    /// <summary>The proration preview awaiting the customer's confirmation, if one was requested.</summary>
    public PlanChangePreview? Preview { get; private set; }

    /// <summary>The outcome of a usage report, shown after recording usage.</summary>
    public UsageReport? UsageReport { get; private set; }

    public string? ErrorMessage { get; private set; }

    public string? StatusMessage { get; private set; }

    public async Task OnGet(CancellationToken cancellationToken) => await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostRecordUsage(
        int subscriptionId,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken)
    {
        if (quantity <= 0m)
        {
            ErrorMessage = "Enter a quantity greater than zero.";
            await LoadAsync(cancellationToken);

            return Page();
        }

        await RunAsync(
            async actor =>
            {
                UsageReport = await _subscriptionService.RecordUsageAsync(
                    actor, subscriptionId, quantity, memo, cancellationToken);

                StatusMessage = UsageReport.PeriodToDateAvailable
                    ? $"Recorded {quantity:N0} unit(s). {UsageReport.PeriodToDateQuantity:N0} unit(s) so far this period will appear on your next invoice."
                    : $"Recorded {quantity:N0} unit(s). They will appear on your next invoice; the running total is temporarily unavailable.";
            },
            cancellationToken);

        await LoadAsync(cancellationToken);

        return Page();
    }

    public async Task<IActionResult> OnPostPreviewPlanChange(
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken)
    {
        await RunAsync(
            async actor => Preview = await _subscriptionService.PreviewPlanChangeAsync(
                actor, subscriptionId, targetPlanHandle, timing, cancellationToken),
            cancellationToken);

        await LoadAsync(cancellationToken);

        return Page();
    }

    public async Task<IActionResult> OnPostChangePlan(
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        long expectedPaymentDueInCents,
        CancellationToken cancellationToken)
    {
        await RunAsync(
            async actor =>
            {
                // The previewed amount travels with the confirmation, so the provider can only
                // apply the change at the figure the customer actually saw.
                var subscription = await _subscriptionService.ChangePlanAsync(
                    actor, subscriptionId, targetPlanHandle, timing, expectedPaymentDueInCents, cancellationToken);

                StatusMessage = timing == PlanChangeTiming.Immediate
                    ? $"Your subscription is now on {subscription.PlanName}."
                    : $"Your subscription will move to '{targetPlanHandle}' at the next renewal.";
            },
            cancellationToken);

        await LoadAsync(cancellationToken);

        return Page();
    }

    public async Task<IActionResult> OnPostLifecycle(
        int subscriptionId,
        SubscriptionLifecycleAction action,
        CancellationTiming cancellationTiming,
        string? reason,
        CancellationToken cancellationToken)
    {
        await RunAsync(
            async actor =>
            {
                var subscription = await _subscriptionService.ApplyLifecycleActionAsync(
                    actor, subscriptionId, action, cancellationTiming, reason, cancellationToken);

                StatusMessage = action == SubscriptionLifecycleAction.Cancel && cancellationTiming == CancellationTiming.EndOfPeriod
                    ? $"Your subscription will end on {subscription.CurrentPeriodEndsAt:d}."
                    : $"Your subscription is now {subscription.State}.";
            },
            cancellationToken);

        await LoadAsync(cancellationToken);

        return Page();
    }

    /// <summary>
    /// Runs a management action, turning each domain failure into a message the customer can act
    /// on. Nothing here throws out of the page.
    /// </summary>
    private async Task RunAsync(Func<SubscriptionActor, Task> action, CancellationToken cancellationToken)
    {
        var userName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            ErrorMessage = "Please sign in again.";
            return;
        }

        try
        {
            await action(SubscriptionActor.Customer(userName));
        }
        catch (SubscriptionStateException exception)
        {
            ErrorMessage = exception.Message;
        }
        catch (StalePlanChangePreviewException exception)
        {
            ErrorMessage = exception.Message;
        }
        catch (BillingConfigurationException exception)
        {
            _logger.LogWarning("Subscription action failed on configuration: {0}", exception.Message);
            ErrorMessage = "That plan is not available right now.";
        }
        catch (BillingProviderException exception)
        {
            _logger.LogWarning("Subscription action was rejected: {0}", exception.Message);
            ErrorMessage = exception.DisplayMessage;
        }
        catch (ArgumentException exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var userName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return;
        }

        try
        {
            Subscriptions = await _subscriptionService.GetSubscriptionsAsync(userName, cancellationToken);
            Plans = await _subscriptionService.GetAvailablePlansAsync(cancellationToken);
        }
        catch (Exception exception) when (
            exception is BillingProviderException or BillingConfigurationException)
        {
            _logger.LogWarning("Could not load subscriptions for {0}: {1}", userName, exception.Message);
            ErrorMessage ??= "Your subscriptions are temporarily unavailable. Please try again shortly.";
        }
    }

    /// <summary>The plans this subscription could move to — every live plan except its own.</summary>
    public IEnumerable<BillingPlan> PlanChangeTargetsFor(Subscription subscription) =>
        Plans.Where(plan => !string.Equals(plan.Handle, subscription.PlanHandle, StringComparison.OrdinalIgnoreCase));
}
