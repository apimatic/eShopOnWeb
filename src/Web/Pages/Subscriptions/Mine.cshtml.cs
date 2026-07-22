using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// UC1/UC2/UC4 — view the signed-in customer's subscriptions, report usage and manage lifecycle.
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

    public UsageReceipt? UsageReceipt { get; private set; }

    public string? StatusMessage { get; private set; }

    public string? ErrorMessage { get; private set; }

    public async Task OnGet(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostRecordUsage(int subscriptionId, decimal quantity, string? memo, CancellationToken cancellationToken)
    {
        await ExecuteAsync(async () =>
        {
            UsageReceipt = await _subscriptionService.RecordUsageAsync(UserReference, subscriptionId, quantity, memo, cancellationToken);
            StatusMessage = UsageReceipt.PeriodToDateTotal.HasValue
                ? $"Recorded {UsageReceipt.Quantity} unit(s). Period to date: {UsageReceipt.PeriodToDateTotal}. This will appear on your next renewal invoice."
                : $"Recorded {UsageReceipt.Quantity} unit(s). The running total is currently unavailable; this will appear on your next renewal invoice.";
        }, cancellationToken);

        return Page();
    }

    public async Task<IActionResult> OnPostLifecycle(int subscriptionId, SubscriptionLifecycleAction action, bool endOfPeriod, CancellationToken cancellationToken)
    {
        await ExecuteAsync(async () =>
        {
            var result = await _subscriptionService.ApplyLifecycleActionAsync(UserReference, subscriptionId, action, endOfPeriod, null, cancellationToken);
            StatusMessage = $"Subscription {subscriptionId} went from {result.PreviousState} to {result.NewState}, effective {result.EffectiveAt:g}.";
        }, cancellationToken);

        return Page();
    }

    private string UserReference => User.Identity!.Name!;

    private async Task ExecuteAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is InvalidSubscriptionStateException or SubscriptionNotFoundException or ArgumentException)
        {
            ErrorMessage = ex.Message;
        }
        catch (BillingConfigurationException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (BillingProviderException)
        {
            ErrorMessage = "The billing service is unavailable right now. Please try again shortly.";
        }

        await LoadAsync(cancellationToken);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            Subscriptions = await _subscriptionService.ListSubscriptionsAsync(UserReference, cancellationToken);
        }
        catch (BillingProviderException)
        {
            ErrorMessage ??= "Your subscriptions are unavailable right now. Please try again shortly.";
        }
    }
}
