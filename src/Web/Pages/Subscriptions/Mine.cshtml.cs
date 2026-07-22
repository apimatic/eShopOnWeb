using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// The signed-in customer's subscriptions, with the lifecycle actions and the plan-change flow.
/// </summary>
[Authorize]
public class MineModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly SubscriptionSettings _settings;

    public MineModel(ISubscriptionService subscriptionService, SubscriptionSettings settings)
    {
        _subscriptionService = subscriptionService;
        _settings = settings;
    }

    public IReadOnlyList<BillingSubscription> Subscriptions { get; private set; } =
        Array.Empty<BillingSubscription>();

    public PlanChangePreview? Preview { get; private set; }

    public string? ErrorMessage { get; private set; }

    public string? StatusMessage { get; private set; }

    public async Task OnGet()
    {
        await LoadSubscriptionsAsync();
    }

    public async Task<IActionResult> OnPostLifecycle(int subscriptionId, SubscriptionLifecycleAction action)
    {
        return await RunAsync(async userName =>
        {
            var updated = await _subscriptionService.ApplyLifecycleActionAsync(userName, subscriptionId, action,
                reason: null);
            StatusMessage = $"Subscription {updated.Id} is now {updated.State}.";
        });
    }

    public async Task<IActionResult> OnPostPreviewPlanChange(int subscriptionId, string targetPlanHandle)
    {
        return await RunAsync(async userName =>
        {
            Preview = await _subscriptionService.PreviewPlanChangeAsync(userName, subscriptionId,
                targetPlanHandle);
        });
    }

    public async Task<IActionResult> OnPostChangePlan(int subscriptionId, string targetPlanHandle,
        PlanChangeTiming timing, decimal? acknowledgedProratedAdjustment)
    {
        return await RunAsync(async userName =>
        {
            var updated = await _subscriptionService.ChangePlanAsync(userName, subscriptionId, targetPlanHandle,
                timing, acknowledgedProratedAdjustment);
            StatusMessage = $"Subscription {updated.Id} is now on plan {updated.PlanHandle}.";
        });
    }

    public async Task<IActionResult> OnPostRecordUsage(int subscriptionId, decimal quantity)
    {
        return await RunAsync(async userName =>
        {
            var report = await _subscriptionService.RecordUsageAsync(userName, subscriptionId, quantity,
                "Reported from the storefront");

            StatusMessage = report.IsPeriodToDateTotalAvailable
                ? $"Recorded {report.Receipt.Quantity} unit(s). {report.PeriodToDateTotal} unit(s) so far this period will appear on your next invoice."
                : $"Recorded {report.Receipt.Quantity} unit(s). They will appear on your next invoice; the running total is unavailable right now.";
        });
    }

    /// <summary>The plan a subscription can be moved to, given the two configured plans.</summary>
    public string? GetAlternatePlanHandle(BillingSubscription subscription)
    {
        if (string.Equals(subscription.PlanHandle, _settings.DefaultProductHandle,
                StringComparison.OrdinalIgnoreCase))
        {
            return _settings.AlternateProductHandle;
        }

        return _settings.DefaultProductHandle;
    }

    private async Task<IActionResult> RunAsync(Func<string, Task> action)
    {
        var userName = GetUserName();

        try
        {
            await action(userName);
        }
        catch (Exception ex) when (IsBillingFailure(ex))
        {
            ErrorMessage = ex.Message;
        }

        await LoadSubscriptionsAsync();
        return Page();
    }

    private async Task LoadSubscriptionsAsync()
    {
        try
        {
            Subscriptions = await _subscriptionService.GetMySubscriptionsAsync(GetUserName());
        }
        catch (Exception ex) when (IsBillingFailure(ex))
        {
            ErrorMessage ??= "Your subscriptions are temporarily unavailable. Please try again shortly.";
            Subscriptions = Array.Empty<BillingSubscription>();
        }
    }

    private string GetUserName()
    {
        Guard.Against.Null(User.Identity, nameof(User.Identity));
        Guard.Against.Null(User.Identity.Name, nameof(User.Identity.Name));

        return User.Identity.Name!;
    }

    private static bool IsBillingFailure(Exception exception) =>
        exception is BillingProviderException
            or BillingConfigurationException
            or InvalidSubscriptionOperationException
            or SubscriptionNotFoundException;
}
