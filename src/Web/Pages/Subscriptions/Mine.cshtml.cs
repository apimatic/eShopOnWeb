using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// The customer's subscription management surface: current state and next billing date (UC1),
/// the pay-as-you-go usage panel (UC2), and the lifecycle actions (UC4).
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

    public IReadOnlyList<CustomerSubscription> Subscriptions { get; private set; } = Array.Empty<CustomerSubscription>();

    public IReadOnlyList<SubscriptionPlan> Plans { get; private set; } = Array.Empty<SubscriptionPlan>();

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostRecordUsageAsync(int subscriptionId, decimal quantity, string? memo)
    {
        if (quantity <= decimal.Zero)
        {
            // Rejected here, so nothing is sent to the billing provider (UC2).
            ErrorMessage = "Enter a usage quantity greater than zero.";
            return RedirectToPage();
        }

        try
        {
            var summary = await _subscriptionService.RecordUsageAsync(UserReference, quantity, memo);

            StatusMessage = summary.IsPeriodTotalAvailable
                ? $"Recorded {summary.Recorded.Quantity:0.##} unit(s). " +
                  $"Period to date: {summary.PeriodToDateQuantity:0.##} unit(s)" +
                  (summary.PeriodToDateAmount.HasValue ? $" ({summary.PeriodToDateAmount.Value:C})" : string.Empty) +
                  ". This will appear on your next renewal invoice."
                : $"Recorded {summary.Recorded.Quantity:0.##} unit(s). It will appear on your next renewal invoice. " +
                  "The running period total is temporarily unavailable.";
        }
        catch (NoActiveSubscriptionException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (BillingConfigurationException ex)
        {
            _logger.LogWarning("Usage could not be recorded because the billing catalog is misconfigured: {0}", ex.Message);
            ErrorMessage = "Usage reporting is not available right now. Please contact support.";
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Usage could not be recorded on subscription {0}: {1}", subscriptionId, ex.ProviderMessage);
            ErrorMessage = "We could not record your usage just now. Please try again shortly.";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostLifecycleAsync(int subscriptionId,
        string action,
        string? cancellationTiming,
        string? reason)
    {
        if (!Enum.TryParse<SubscriptionLifecycleAction>(action, ignoreCase: true, out var lifecycleAction)
            || !Enum.IsDefined(lifecycleAction))
        {
            ErrorMessage = "That subscription action is not recognised.";
            return RedirectToPage();
        }

        var timing = string.Equals(cancellationTiming, nameof(CancellationTiming.EndOfPeriod), StringComparison.OrdinalIgnoreCase)
            ? CancellationTiming.EndOfPeriod
            : CancellationTiming.Immediate;

        try
        {
            var updated = await _subscriptionService.ApplyLifecycleActionAsync(UserReference,
                subscriptionId,
                lifecycleAction,
                timing,
                reason);

            StatusMessage = DescribeOutcome(lifecycleAction, timing, updated);
        }
        catch (InvalidSubscriptionTransitionException ex)
        {
            // Rejected locally, with the legal alternatives; no provider call was made.
            ErrorMessage = ex.Message;
        }
        catch (SubscriptionNotFoundException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (BillingProviderException ex)
        {
            // The provider's view is the truth — reloading shows whatever state it really holds.
            _logger.LogWarning("Lifecycle action {0} failed on subscription {1}: {2}",
                lifecycleAction, subscriptionId, ex.ProviderMessage);
            ErrorMessage = $"The billing provider refused that change: {ex.ProviderMessage}";
        }

        return RedirectToPage();
    }

    private string UserReference
    {
        get
        {
            Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
            return User.Identity.Name;
        }
    }

    private static string DescribeOutcome(SubscriptionLifecycleAction action,
        CancellationTiming timing,
        CustomerSubscription updated)
    {
        if (action == SubscriptionLifecycleAction.Cancel && timing == CancellationTiming.EndOfPeriod)
        {
            var effective = updated.ScheduledCancellationAt ?? updated.CurrentPeriodEndsAt;
            return effective.HasValue
                ? $"Your subscription will be cancelled on {effective.Value:d} and stays active until then."
                : "Your subscription is scheduled to cancel at the end of the current period.";
        }

        return $"Subscription {updated.Id} is now {updated.State}.";
    }

    private async Task LoadAsync()
    {
        try
        {
            Subscriptions = await _subscriptionService.ListMySubscriptionsAsync(UserReference);
        }
        catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException)
        {
            _logger.LogWarning("Subscriptions could not be listed: {0}", ex.Message);
            ErrorMessage ??= "Your subscriptions are unavailable right now. Please try again shortly.";
            return;
        }

        try
        {
            Plans = await _subscriptionService.ListPlansAsync();
        }
        catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException)
        {
            // Plan-change targets are a convenience here; failing to list them must not hide the
            // subscriptions themselves.
            _logger.LogWarning("Plans could not be listed for the management page: {0}", ex.Message);
        }
    }
}
