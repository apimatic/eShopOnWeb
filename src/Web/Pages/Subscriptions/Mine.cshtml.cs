using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Web.ViewModels.Subscriptions;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// The customer's single management surface: their subscriptions (UC1), pay-as-you-go usage (UC2),
/// plan change with a confirmed proration preview (UC3), and the lifecycle actions (UC4).
/// </summary>
[Authorize]
public class MineModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;

    public MineModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public MySubscriptionsViewModel SubscriptionsModel { get; set; } = new MySubscriptionsViewModel();

    public string? ErrorMessage { get; set; }

    [TempData]
    public string? SubscriptionStatus { get; set; }

    public async Task OnGet()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostRecordUsage(int quantity, string? memo)
    {
        try
        {
            var summary = await _subscriptionService.RecordUsageAsync(GetUserName(), quantity, memo);

            SubscriptionStatus = summary.PeriodToDateUnits.HasValue
                ? $"Recorded {summary.Receipt.Quantity:N0} unit(s). Period to date: {summary.PeriodToDateUnits:N0} unit(s), " +
                  $"${summary.PeriodToDateCharge:N2}. This appears on your next renewal invoice."
                : $"Recorded {summary.Receipt.Quantity:N0} unit(s). It will appear on your next renewal invoice " +
                  "(the running total is temporarily unavailable).";

            return RedirectToPage();
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            ErrorMessage = Describe(ex);
            await LoadAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostPreviewPlanChange(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing)
    {
        try
        {
            SubscriptionsModel.PendingPreview = await _subscriptionService.PreviewPlanChangeAsync(
                GetUserName(), subscriptionId, targetPlanHandle, timing);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            ErrorMessage = Describe(ex);
        }

        await LoadAsync(preservePreview: true);
        return Page();
    }

    public async Task<IActionResult> OnPostChangePlan(int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        string? fingerprint)
    {
        try
        {
            var result = await _subscriptionService.ChangePlanAsync(
                GetUserName(), subscriptionId, targetPlanHandle, timing, fingerprint);

            var effective = result.EffectiveAt is null
                ? "immediately"
                : $"on {result.EffectiveAt.Value.ToLocalTime():d}";

            SubscriptionStatus =
                $"Plan changed from {result.PreviousPlanHandle} to {result.NewPlanHandle} {effective}. " +
                $"Proration applied: ${result.ProrationAmount:N2}.";

            return RedirectToPage();
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            ErrorMessage = Describe(ex);
            await LoadAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostLifecycle(int subscriptionId,
        SubscriptionLifecycleAction action,
        CancellationTiming timing,
        string? reason)
    {
        try
        {
            var updated = await _subscriptionService.ApplyLifecycleActionAsync(
                GetUserName(), subscriptionId, action, timing, reason);

            var effective = updated.CancelAtEndOfPeriod && updated.DelayedCancelAt is not null
                ? $" Effective {updated.DelayedCancelAt.Value.ToLocalTime():d}."
                : string.Empty;

            SubscriptionStatus = $"Subscription {updated.Id} is now {updated.State}.{effective}";

            return RedirectToPage();
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            ErrorMessage = Describe(ex);
            await LoadAsync();
            return Page();
        }
    }

    private async Task LoadAsync(bool preservePreview = false)
    {
        var preview = preservePreview ? SubscriptionsModel.PendingPreview : null;
        var userName = GetUserName();

        try
        {
            SubscriptionsModel = new MySubscriptionsViewModel
            {
                Subscriptions = (await _subscriptionService.GetSubscriptionsAsync(userName)).ToList(),
                PendingPreview = preview
            };
        }
        catch (BillingProviderException ex)
        {
            ErrorMessage ??= $"Your subscriptions are unavailable right now. {ex.ProviderMessage}";
            SubscriptionsModel = new MySubscriptionsViewModel { PendingPreview = preview };
            return;
        }

        // The plan list and the metered component are supporting detail: if either is unavailable the
        // customer still sees their subscriptions.
        try
        {
            SubscriptionsModel.Plans = (await _subscriptionService.GetPlansAsync()).ToList();
        }
        catch (BillingProviderException)
        {
            SubscriptionsModel.Plans = new List<SubscriptionPlan>();
        }

        try
        {
            SubscriptionsModel.MeteredComponent = await _subscriptionService.GetMeteredComponentAsync();
        }
        catch (BillingProviderException)
        {
            SubscriptionsModel.MeteredComponent = null;
        }

        var active = SubscriptionsModel.ActiveSubscription;
        if (active is not null)
        {
            try
            {
                SubscriptionsModel.PeriodToDateUnits = await _subscriptionService.GetPeriodToDateUsageAsync(userName, active.Id);
            }
            catch (BillingProviderException)
            {
                SubscriptionsModel.PeriodToDateUnits = null;
            }
        }
    }

    private string GetUserName()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        return User.Identity.Name!;
    }

    /// <summary>The failures a customer can legitimately provoke, all of which are shown rather than thrown.</summary>
    private static bool IsExpected(Exception exception) => exception is
        BillingProviderException or
        SubscriptionNotFoundException or
        SubscriptionNotBillableException or
        InvalidSubscriptionTransitionException or
        PlanChangeNotAllowedException or
        StalePlanChangePreviewException or
        ArgumentException;

    private static string Describe(Exception exception) => exception switch
    {
        BillingProviderException billing => billing.ProviderMessage,
        ArgumentException => "That quantity is not valid. Enter a whole number of units greater than zero.",
        _ => exception.Message
    };
}
