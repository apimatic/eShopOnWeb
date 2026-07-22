using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// The customer's own subscription management surface: state, plan change and the lifecycle
/// actions (UC1, UC3, UC4), plus the minimal usage panel (UC2).
/// </summary>
[Authorize]
public class MineModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;

    public MineModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public IReadOnlyCollection<Subscription> Subscriptions { get; private set; } = Array.Empty<Subscription>();

    public BillingPlanChangePreview? Preview { get; private set; }

    public UsageReportResult? Usage { get; private set; }

    public string? ErrorMessage { get; private set; }

    public string? StatusMessage { get; private set; }

    public async Task OnGet()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostPreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing)
    {
        return await RunAsync(async () =>
        {
            Preview = await _subscriptionService.PreviewPlanChangeAsync(subscriptionId, targetPlanHandle, timing);
        });
    }

    public async Task<IActionResult> OnPostChangePlanAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing, decimal? confirmedPaymentDue)
    {
        return await RunAsync(async () =>
        {
            var result = await _subscriptionService.ChangePlanAsync(subscriptionId, targetPlanHandle, timing, confirmedPaymentDue);
            StatusMessage = $"Moved from {result.OldPlanHandle} to {result.NewPlanHandle}, effective {result.EffectiveAt:d}.";
        });
    }

    public async Task<IActionResult> OnPostLifecycleAsync(int subscriptionId, SubscriptionLifecycleAction action, CancellationTiming timing, string? reason)
    {
        return await RunAsync(async () =>
        {
            var subscription = await _subscriptionService.ApplyLifecycleActionAsync(subscriptionId, action, timing, reason);
            StatusMessage = $"Subscription is now {subscription.State}.";
        });
    }

    public async Task<IActionResult> OnPostRecordUsageAsync(int subscriptionId, decimal quantity, string? memo)
    {
        return await RunAsync(async () =>
        {
            Usage = await _subscriptionService.RecordUsageAsync(subscriptionId, quantity, memo);
            StatusMessage = Usage.PeriodToDateAvailable
                ? $"Recorded {Usage.QuantityRecorded} units; {Usage.PeriodToDateUnits} units accrued this period, billed on the next renewal invoice."
                : $"Recorded {Usage.QuantityRecorded} units; the running total is unavailable right now.";
        });
    }

    private async Task<IActionResult> RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException or ArgumentException)
        {
            ErrorMessage = ex.Message;
        }

        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        try
        {
            Subscriptions = await _subscriptionService.ListSubscriptionsAsync(UserReference());
        }
        catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException)
        {
            ErrorMessage ??= "Your subscriptions are unavailable right now. Please try again shortly.";
            Subscriptions = Array.Empty<Subscription>();
        }
    }

    private string UserReference()
    {
        Guard.Against.Null(User.Identity, nameof(User.Identity));
        Guard.Against.NullOrWhiteSpace(User.Identity.Name, nameof(User.Identity.Name));

        return User.Identity.Name!;
    }
}
