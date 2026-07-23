using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// UC1 — browse the available plans and subscribe. Customer-facing subscription pages are authorized with
/// the storefront's existing cookie identity (plan.md §2.4).
/// </summary>
[Authorize]
public class PlansModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;

    public PlansModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public IReadOnlyList<BillingPlan> Plans { get; private set; } = Array.Empty<BillingPlan>();

    /// <summary>The plan the signed-in customer is already on, if any.</summary>
    public Subscription? CurrentSubscription { get; private set; }

    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostSubscribeAsync(string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            await LoadAsync(cancellationToken);
            ErrorMessage = "Choose a plan to subscribe to.";
            return Page();
        }

        try
        {
            var subscription = await _subscriptionService.SubscribeAsync(UserReference(), planHandle, cancellationToken);
            return RedirectToPage("./Mine", new { highlight = subscription.Id });
        }
        catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException
                                       or InvalidSubscriptionOperationException)
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
            CurrentSubscription = await _subscriptionService.FindActiveSubscriptionAsync(UserReference(), cancellationToken);
        }
        catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException)
        {
            // UC1 failure scenario: plans cannot be listed. Show a friendly message; attempt no enrollment.
            Plans = Array.Empty<BillingPlan>();
            ErrorMessage = "Subscription plans are unavailable right now. Please try again shortly.";
        }
    }

    private string UserReference()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        return User.Identity!.Name!;
    }
}
