using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// UC1 steps 1–2: browse the available recurring plans and subscribe to one.
/// </summary>
[Authorize]
public class PlansModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;

    public PlansModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public IReadOnlyCollection<SubscriptionPlan> Plans { get; private set; } = Array.Empty<SubscriptionPlan>();

    /// <summary>The plan handles this customer is already enrolled in, so the page can say so.</summary>
    public IReadOnlyCollection<string> ActivePlanHandles { get; private set; } = Array.Empty<string>();

    public string? ErrorMessage { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGet(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostSubscribe(string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            await LoadAsync(cancellationToken);
            ErrorMessage = "Choose a plan to subscribe to.";

            return Page();
        }

        try
        {
            var subscription = await _subscriptionService.SubscribeAsync(GetUserReference(), planHandle, cancellationToken);

            StatusMessage = $"You are subscribed to {subscription.PlanName} ({subscription.State}).";

            return RedirectToPage("./Mine");
        }
        catch (Exception ex) when (ex is BillingProviderException or InvalidSubscriptionOperationException)
        {
            await LoadAsync(cancellationToken);
            ErrorMessage = ex.Message;

            return Page();
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            Plans = await _subscriptionService.ListPlansAsync(cancellationToken);

            var subscriptions = await _subscriptionService.ListSubscriptionsAsync(GetUserReference(), cancellationToken);
            ActivePlanHandles = subscriptions.Where(s => s.IsActive).Select(s => s.PlanHandle).ToArray();
        }
        catch (BillingProviderException ex)
        {
            // UC1 failure scenario: plans cannot be listed. Show a friendly error and attempt nothing.
            Plans = Array.Empty<SubscriptionPlan>();
            ErrorMessage = $"Subscription plans are unavailable right now. {ex.Message}";
        }
    }

    private string GetUserReference()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));

        return User.Identity.Name!;
    }
}
