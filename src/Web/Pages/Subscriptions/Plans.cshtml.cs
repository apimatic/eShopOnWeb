using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// UC1 — browse the available recurring plans and subscribe to one.
/// </summary>
/// <remarks>
/// Browsing is anonymous so shoppers can compare plans before signing in; subscribing requires an
/// authenticated session, and the signed-in user is taken from the cookie the way the rest of the
/// storefront does it (plan.md §2.4).
/// </remarks>
public class PlansModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;

    public PlansModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public IReadOnlyList<SubscriptionPlan> Plans { get; private set; } = Array.Empty<SubscriptionPlan>();

    /// <summary>Handles of plans the signed-in customer already holds a live subscription for.</summary>
    public IReadOnlyCollection<string> SubscribedPlanHandles { get; private set; } = Array.Empty<string>();

    [TempData]
    public string? StatusMessage { get; set; }

    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostSubscribeAsync(string planHandle, CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(User.Identity.Name))
        {
            return RedirectToPage("/Account/Login", new { area = "Identity" });
        }

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            ErrorMessage = "Choose a plan to subscribe to.";
            await LoadAsync(cancellationToken);
            return Page();
        }

        try
        {
            var subscription = await _subscriptionService.SubscribeAsync(
                User.Identity.Name, planHandle, cancellationToken);

            StatusMessage =
                $"You are subscribed to {subscription.PlanName ?? subscription.PlanHandle} " +
                $"(${subscription.PlanPrice:N2} per period). Current state: {subscription.State}.";

            return RedirectToPage("./Mine");
        }
        catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException)
        {
            ErrorMessage = ex.Message;
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            Plans = await _subscriptionService.ListPlansAsync(cancellationToken);

            if (User.Identity?.IsAuthenticated == true && !string.IsNullOrEmpty(User.Identity.Name))
            {
                var mine = await _subscriptionService.ListSubscriptionsAsync(User.Identity.Name, cancellationToken);
                SubscribedPlanHandles = mine
                    .Where(subscription => subscription.IsLive && subscription.PlanHandle is not null)
                    .Select(subscription => subscription.PlanHandle!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException)
        {
            // UC1: plans that cannot be listed show a friendly error; no enrolment is attempted.
            Plans = Array.Empty<SubscriptionPlan>();
            ErrorMessage ??= $"Subscription plans are unavailable right now. {ex.Message}";
        }
    }
}
