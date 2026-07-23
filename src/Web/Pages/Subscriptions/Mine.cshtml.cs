using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// View and manage the signed-in customer's subscription: usage (UC2), plan change (UC3) and the
/// lifecycle actions (UC4).
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

    public IReadOnlyCollection<BillingSubscription> Subscriptions { get; private set; }
        = Array.Empty<BillingSubscription>();

    public BillingSubscription? Current => Subscriptions.FirstOrDefault(subscription => subscription.IsLive)
        ?? Subscriptions.OrderByDescending(subscription => subscription.Id).FirstOrDefault();

    public IReadOnlyCollection<BillingPlan> Plans { get; private set; } = Array.Empty<BillingPlan>();

    public decimal? PeriodToDateUsage { get; private set; }

    /// <summary>The proration quote awaiting the customer's confirmation (UC3, step 3).</summary>
    public PlanChangePreview? Preview { get; private set; }

    public string? ErrorMessage { get; private set; }

    public string? StatusMessage { get; private set; }

    public async Task OnGet()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostRecordUsage(decimal quantity)
    {
        var userReference = RequireUserReference();

        try
        {
            var usage = await _subscriptionService.RecordUsageAsync(userReference, quantity, "Reported from storefront");

            StatusMessage = usage.PeriodToDateTotal.HasValue
                ? $"Recorded {quantity:N0} units. Period-to-date total is {usage.PeriodToDateTotal.Value:N0}. " +
                  "This will appear on your next renewal invoice."
                : $"Recorded {quantity:N0} units. This will appear on your next renewal invoice.";
        }
        catch (ArgumentOutOfRangeException)
        {
            ErrorMessage = "Enter a usage quantity greater than zero.";
        }
        catch (Exception exception) when (IsExpectedSubscriptionFailure(exception))
        {
            ErrorMessage = Describe(exception);
        }

        await LoadAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostPreviewPlanChange(string targetPlanHandle)
    {
        var userReference = RequireUserReference();

        try
        {
            Preview = await _subscriptionService.PreviewPlanChangeAsync(userReference, targetPlanHandle);
        }
        catch (Exception exception) when (IsExpectedSubscriptionFailure(exception))
        {
            ErrorMessage = Describe(exception);
        }

        await LoadAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostChangePlan(string targetPlanHandle,
        string timing,
        decimal? expectedPaymentDue)
    {
        var userReference = RequireUserReference();

        var planChangeTiming = string.Equals(timing, nameof(PlanChangeTiming.AtNextRenewal),
            StringComparison.OrdinalIgnoreCase)
            ? PlanChangeTiming.AtNextRenewal
            : PlanChangeTiming.Immediate;

        try
        {
            var subscription = await _subscriptionService.ChangePlanAsync(userReference, targetPlanHandle,
                planChangeTiming, expectedPaymentDue);

            StatusMessage = planChangeTiming == PlanChangeTiming.AtNextRenewal
                ? $"Your plan will change to {targetPlanHandle} at your next renewal."
                : $"Your plan is now {subscription.PlanHandle}.";
        }
        catch (StalePlanChangePreviewException staleException)
        {
            // Never charge an amount the customer did not confirm (UC3 failure path).
            _logger.LogWarning(staleException.Message);
            ErrorMessage = "The cost of this change has changed since you previewed it. Please preview it again.";
        }
        catch (Exception exception) when (IsExpectedSubscriptionFailure(exception))
        {
            ErrorMessage = Describe(exception);
        }

        await LoadAsync();

        return Page();
    }

    public Task<IActionResult> OnPostPause() =>
        ApplyLifecycleAsync(userReference => _subscriptionService.PauseAsync(userReference),
            "Your subscription is paused.");

    public Task<IActionResult> OnPostResume() =>
        ApplyLifecycleAsync(userReference => _subscriptionService.ResumeAsync(userReference),
            "Your subscription is active again.");

    public Task<IActionResult> OnPostReactivate() =>
        ApplyLifecycleAsync(userReference => _subscriptionService.ReactivateAsync(userReference),
            "Your subscription has been reactivated.");

    public Task<IActionResult> OnPostCancel(bool cancelAtEndOfPeriod)
    {
        var timing = cancelAtEndOfPeriod ? CancellationTiming.EndOfBillingPeriod : CancellationTiming.Immediate;

        var message = cancelAtEndOfPeriod
            ? "Your subscription will be cancelled at the end of the current billing period."
            : "Your subscription has been cancelled.";

        return ApplyLifecycleAsync(
            userReference => _subscriptionService.CancelAsync(userReference, timing, "Cancelled from storefront"),
            message);
    }

    private async Task<IActionResult> ApplyLifecycleAsync(
        Func<string, Task<BillingSubscription>> transition,
        string successMessage)
    {
        var userReference = RequireUserReference();

        try
        {
            await transition(userReference);
            StatusMessage = successMessage;
        }
        catch (Exception exception) when (IsExpectedSubscriptionFailure(exception))
        {
            ErrorMessage = Describe(exception);
        }

        await LoadAsync();

        return Page();
    }

    private async Task LoadAsync()
    {
        var userReference = RequireUserReference();

        try
        {
            Subscriptions = await _subscriptionService.GetSubscriptionsForUserAsync(userReference);
        }
        catch (BillingProviderException providerException)
        {
            _logger.LogWarning($"Could not read subscriptions: {providerException.Message}");
            Subscriptions = Array.Empty<BillingSubscription>();
            ErrorMessage ??= "Your subscription details are unavailable right now. Please try again shortly.";
            return;
        }

        await LoadPlansAsync();
        await LoadUsageAsync(userReference);
    }

    private async Task LoadPlansAsync()
    {
        try
        {
            Plans = await _subscriptionService.GetAvailablePlansAsync();
        }
        catch (BillingProviderException providerException)
        {
            _logger.LogWarning($"Could not list plans: {providerException.Message}");
            Plans = Array.Empty<BillingPlan>();
        }
    }

    private async Task LoadUsageAsync(string userReference)
    {
        if (Current is null || !Current.IsLive)
        {
            return;
        }

        try
        {
            PeriodToDateUsage = await _subscriptionService.GetPeriodToDateUsageAsync(userReference);
        }
        catch (Exception exception) when (IsExpectedSubscriptionFailure(exception))
        {
            // A usage figure we cannot read must not blank out the rest of the page.
            _logger.LogWarning($"Could not read period-to-date usage: {exception.Message}");
        }
    }

    private string RequireUserReference()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));

        return User.Identity.Name;
    }

    private static bool IsExpectedSubscriptionFailure(Exception exception) =>
        exception is BillingProviderException
            or BillingConfigurationException
            or InvalidSubscriptionTransitionException
            or NoActiveSubscriptionException;

    private string Describe(Exception exception)
    {
        _logger.LogWarning(exception.Message);

        return exception switch
        {
            // These two describe the customer's own subscription, so the real message is useful.
            InvalidSubscriptionTransitionException => exception.Message,
            NoActiveSubscriptionException => "You do not have a subscription yet.",

            BillingProviderValidationException validationException => validationException.Message,

            // Configuration and transport failures are ours, not the customer's.
            _ => "We could not complete that request. Please try again shortly."
        };
    }
}
