using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// The signed-in customer's subscription management surface: current subscriptions and their next
/// billing date (UC1), the pay-as-you-go usage panel (UC2), plan change with a proration preview
/// they must confirm (UC3), and the four lifecycle actions (UC4).
/// </summary>
[Authorize]
public class MineModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly SubscriptionSettings _settings;
    private readonly IAppLogger<MineModel> _logger;

    public MineModel(ISubscriptionService subscriptionService,
        IOptions<SubscriptionSettings> settings,
        IAppLogger<MineModel> logger)
    {
        _subscriptionService = subscriptionService;
        _settings = settings.Value;
        _logger = logger;
    }

    public IReadOnlyCollection<CustomerSubscription> Subscriptions { get; private set; } =
        Array.Empty<CustomerSubscription>();

    /// <summary>Period-to-date metered usage, keyed by subscription id.</summary>
    public IDictionary<int, ComponentUsageSummary> Usage { get; } = new Dictionary<int, ComponentUsageSummary>();

    /// <summary>The pending proration preview the customer is being asked to confirm, if any.</summary>
    public PlanChangePreview? PendingPreview { get; private set; }

    public int PendingPreviewSubscriptionId { get; private set; }

    public string? ErrorMessage { get; private set; }

    public string? StatusMessage { get; private set; }

    public async Task OnGet(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostRecordUsage(int subscriptionId, decimal quantity, string? memo,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(async () =>
        {
            var report = await _subscriptionService.RecordUsageAsync(UserReference(), subscriptionId,
                quantity, memo, cancellationToken);

            StatusMessage = report.IsTotalAvailable
                ? $"Recorded {report.Recorded.Quantity:N0} unit(s). " +
                  $"Period to date: {report.Usage!.UnitBalance:N0} unit(s), " +
                  $"about ${report.Usage.EstimatedCharge ?? 0m:N2} on your next renewal invoice."
                : $"Recorded {report.Recorded.Quantity:N0} unit(s). It will appear on your next renewal " +
                  "invoice; the running total is temporarily unavailable.";
        }, cancellationToken);

        return Page();
    }

    public async Task<IActionResult> OnPostPreviewPlanChange(int subscriptionId, string targetPlanHandle,
        string timing, CancellationToken cancellationToken)
    {
        await ExecuteAsync(async () =>
        {
            var planChangeTiming = ParseTiming(timing);

            PendingPreview = await _subscriptionService.PreviewPlanChangeAsync(UserReference(), subscriptionId,
                targetPlanHandle, planChangeTiming, cancellationToken);
            PendingPreviewSubscriptionId = subscriptionId;
        }, cancellationToken);

        return Page();
    }

    public async Task<IActionResult> OnPostChangePlan(int subscriptionId, string targetPlanHandle, string timing,
        long confirmedPaymentDueInCents, CancellationToken cancellationToken)
    {
        await ExecuteAsync(async () =>
        {
            var planChangeTiming = ParseTiming(timing);

            var subscription = await _subscriptionService.ChangePlanAsync(UserReference(), subscriptionId,
                targetPlanHandle, planChangeTiming, confirmedPaymentDueInCents, cancellationToken);

            StatusMessage = planChangeTiming == PlanChangeTiming.AtNextRenewal
                ? $"Your plan will change to {targetPlanHandle} on " +
                  $"{subscription.NextBillingDate?.ToString("d") ?? "your next renewal"}."
                : $"Your plan is now {subscription.PlanHandle ?? targetPlanHandle}.";
        }, cancellationToken);

        return Page();
    }

    public async Task<IActionResult> OnPostLifecycle(int subscriptionId, string action, string? cancellationTiming,
        string? reason, CancellationToken cancellationToken)
    {
        await ExecuteAsync(async () =>
        {
            if (!Enum.TryParse<SubscriptionLifecycleAction>(action, ignoreCase: true, out var lifecycleAction))
            {
                throw new InvalidSubscriptionOperationException($"'{action}' is not a lifecycle action.");
            }

            var timing = string.Equals(cancellationTiming, nameof(CancellationTiming.EndOfPeriod),
                StringComparison.OrdinalIgnoreCase)
                ? CancellationTiming.EndOfPeriod
                : CancellationTiming.Immediate;

            var subscription = await _subscriptionService.ApplyLifecycleActionAsync(UserReference(), subscriptionId,
                lifecycleAction, timing, reason, cancellationToken);

            StatusMessage = lifecycleAction == SubscriptionLifecycleAction.Cancel &&
                            timing == CancellationTiming.EndOfPeriod
                ? $"Your subscription will end on " +
                  $"{subscription.DelayedCancelAt?.ToString("d") ?? subscription.CurrentPeriodEndsAt?.ToString("d") ?? "the end of the period"}."
                : $"Your subscription is now {subscription.ProviderState ?? subscription.Status.ToString()}.";
        }, cancellationToken);

        return Page();
    }

    /// <summary>
    /// The plan this subscription can move to — the other end of the configured upgrade/downgrade
    /// pair, or <c>null</c> when it is already on the only alternative.
    /// </summary>
    public string? TargetPlanFor(CustomerSubscription subscription)
    {
        var isOnDefault = string.Equals(subscription.PlanHandle, _settings.DefaultProductHandle,
            StringComparison.OrdinalIgnoreCase);

        var target = isOnDefault ? _settings.AlternateProductHandle : _settings.DefaultProductHandle;

        return string.IsNullOrWhiteSpace(target) ||
               string.Equals(target, subscription.PlanHandle, StringComparison.OrdinalIgnoreCase)
            ? null
            : target;
    }

    /// <summary>The lifecycle actions that are legal from a subscription's current state.</summary>
    public IReadOnlyCollection<SubscriptionLifecycleAction> AvailableActionsFor(CustomerSubscription subscription)
    {
        var isPaused = subscription.Status == SubscriptionStatus.OnHold || subscription.Status == SubscriptionStatus.Paused;
        var isTerminated = subscription.Status == SubscriptionStatus.Canceled ||
                           subscription.Status == SubscriptionStatus.Expired;

        var actions = new List<SubscriptionLifecycleAction>();

        if (subscription.IsActive)
        {
            actions.Add(SubscriptionLifecycleAction.Pause);
        }

        if (isPaused)
        {
            actions.Add(SubscriptionLifecycleAction.Resume);
        }

        if (isTerminated)
        {
            actions.Add(SubscriptionLifecycleAction.Reactivate);
        }
        else
        {
            actions.Add(SubscriptionLifecycleAction.Cancel);
        }

        return actions;
    }

    private string UserReference()
    {
        Guard.Against.Null(User.Identity?.Name, nameof(User.Identity.Name));
        return User.Identity!.Name!;
    }

    private static PlanChangeTiming ParseTiming(string? timing)
    {
        return string.Equals(timing, nameof(PlanChangeTiming.AtNextRenewal), StringComparison.OrdinalIgnoreCase)
            ? PlanChangeTiming.AtNextRenewal
            : PlanChangeTiming.Immediate;
    }

    /// <summary>
    /// Runs a management action, turning every expected domain failure into a message the customer
    /// can act on, then reloads the page data so what they see matches the provider's current state.
    /// </summary>
    private async Task ExecuteAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        try
        {
            await action();
        }
        catch (InvalidSubscriptionOperationException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (StalePlanChangePreviewException ex)
        {
            _logger.LogWarning("Plan change refused as stale: {0}", ex.Message);
            ErrorMessage = "The cost of this change has moved since it was quoted. Please preview it again.";
        }
        catch (SubscriptionNotFoundException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (BillingConfigurationException ex)
        {
            _logger.LogWarning("Subscription action failed on configuration: {0}", ex.Message);
            ErrorMessage = "This action is not available right now. Please contact support.";
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Subscription action was rejected by the billing provider: {0}", ex.ProviderMessage);
            ErrorMessage = "The billing provider could not complete that action. Please try again shortly.";
        }

        await LoadAsync(cancellationToken);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            Subscriptions = await _subscriptionService.ListSubscriptionsAsync(UserReference(), cancellationToken);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Subscriptions could not be listed: {0}", ex.ProviderMessage);
            ErrorMessage ??= "Your subscriptions are temporarily unavailable. Please try again shortly.";
            return;
        }
        catch (BillingConfigurationException ex)
        {
            _logger.LogWarning("Subscriptions could not be listed because of configuration: {0}", ex.Message);
            ErrorMessage ??= "Subscriptions are not configured. Please contact support.";
            return;
        }

        foreach (var subscription in Subscriptions.Where(s => s.IsActive))
        {
            // Usage is a nice-to-have on this page; a failure to read it must not hide the
            // subscriptions themselves.
            try
            {
                var usage = await _subscriptionService.GetUsageAsync(UserReference(), subscription.Id, cancellationToken);
                if (usage is not null)
                {
                    Usage[subscription.Id] = usage;
                }
            }
            catch (BillingProviderException ex)
            {
                _logger.LogWarning("Usage for subscription {0} could not be read: {1}", subscription.Id, ex.ProviderMessage);
            }
            catch (BillingConfigurationException ex)
            {
                _logger.LogWarning("Usage for subscription {0} is unavailable: {1}", subscription.Id, ex.Message);
            }
        }
    }
}
