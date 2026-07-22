using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// The customer's subscription management surface: view state and usage (UC1/UC2), preview and
/// commit a plan change (UC3), and run lifecycle actions (UC4).
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

    public IReadOnlyCollection<SubscriptionViewModel> Subscriptions { get; private set; } =
        Array.Empty<SubscriptionViewModel>();

    /// <summary>
    /// The quote awaiting the customer's confirmation, shown after a preview (UC3 step 3).
    /// </summary>
    public PlanChangePreview? PendingPreview { get; private set; }

    public int PendingPreviewSubscriptionId { get; private set; }

    public string? ErrorMessage { get; private set; }

    public string? StatusMessage { get; private set; }

    public async Task OnGet()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostRecordUsage(int subscriptionId, decimal quantity, string? memo)
    {
        await ExecuteAsync(async () =>
        {
            var report = await _subscriptionService.RecordUsageForSubscriptionAsync(subscriptionId, quantity, memo);
            StatusMessage = report.BalanceUnavailable
                ? $"Recorded {report.Record.Quantity} unit(s). The running total is currently unavailable; " +
                  "the charge will still appear on your next renewal invoice."
                : $"Recorded {report.Record.Quantity} unit(s). {report.PeriodToDateBalance} unit(s) so far this " +
                  "period will appear on your next renewal invoice.";
        });

        return Page();
    }

    public async Task<IActionResult> OnPostPreviewPlanChange(int subscriptionId, string targetPlanHandle,
        PlanChangeTiming timing)
    {
        await ExecuteAsync(async () =>
        {
            PendingPreview = await _subscriptionService.PreviewPlanChangeAsync(subscriptionId, targetPlanHandle, timing);
            PendingPreviewSubscriptionId = subscriptionId;
        });

        return Page();
    }

    public async Task<IActionResult> OnPostChangePlan(int subscriptionId, string targetPlanHandle,
        PlanChangeTiming timing, long proratedAdjustmentInCents, long chargeInCents, long paymentDueInCents,
        long creditAppliedInCents)
    {
        await ExecuteAsync(async () =>
        {
            // The amounts the customer was shown are posted back and re-checked against a fresh
            // quote, so a change is never committed at a different price (UC3).
            var confirmed = new PlanChangePreview(targetPlanHandle, timing, proratedAdjustmentInCents,
                chargeInCents, paymentDueInCents, creditAppliedInCents);

            var changed = await _subscriptionService.ChangePlanAsync(subscriptionId, targetPlanHandle, timing, confirmed);

            StatusMessage = timing == PlanChangeTiming.Immediately
                ? $"Moved to {changed.PlanName}. {confirmed.PaymentDue:C} was due, effective now."
                : $"{targetPlanHandle} takes effect at your next renewal on {changed.CurrentPeriodEndsAt:d}.";
        });

        return Page();
    }

    public async Task<IActionResult> OnPostLifecycle(int subscriptionId, SubscriptionLifecycleAction action,
        bool endOfPeriod, string? reason)
    {
        await ExecuteAsync(async () =>
        {
            var result = await _subscriptionService.ApplyLifecycleActionAsync(subscriptionId, action, endOfPeriod, reason);

            StatusMessage = result.CancelAtEndOfPeriod
                ? $"Subscription {result.Id} is {result.State} and will cancel on {result.CurrentPeriodEndsAt:d}."
                : $"Subscription {result.Id} is now {result.State}.";
        });

        return Page();
    }

    /// <summary>
    /// The other plan the customer can move to, so the page always offers exactly one target (UC3).
    /// </summary>
    public string OtherPlanHandle(string currentPlanHandle) =>
        currentPlanHandle == _settings.DefaultProductHandle
            ? _settings.AlternateProductHandle
            : _settings.DefaultProductHandle;

    /// <summary>
    /// Runs a page action, turning the domain's typed failures into a message the customer can act
    /// on, then refreshes the view from the provider.
    /// </summary>
    private async Task ExecuteAsync(Func<Task> action)
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));

        try
        {
            await action();
        }
        catch (InvalidSubscriptionTransitionException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (InvalidPlanChangeException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (StalePlanChangePreviewException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (NoActiveSubscriptionException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (BillingConfigurationException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (BillingProviderException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }

        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));

        try
        {
            var subscriptions = await _subscriptionService.GetMySubscriptionsAsync(User.Identity.Name!);

            var models = new List<SubscriptionViewModel>();
            foreach (var subscription in subscriptions)
            {
                // Usage only accrues on a live subscription, so don't read a balance for the others.
                decimal? balance = null;
                if (subscription.IsLive)
                {
                    balance = await _subscriptionService.GetUsageBalanceAsync(subscription.Id);
                }

                models.Add(new SubscriptionViewModel(subscription, balance));
            }

            Subscriptions = models;
        }
        catch (BillingProviderException ex)
        {
            ErrorMessage ??= ex.Message;
        }
        catch (BillingConfigurationException ex)
        {
            ErrorMessage ??= ex.Message;
        }
    }
}
