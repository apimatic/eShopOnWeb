using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

[Authorize]
public class PlansModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;

    public PlansModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public IReadOnlyCollection<BillingPlan> Plans { get; private set; } = new List<BillingPlan>();

    public Subscription? CurrentSubscription { get; private set; }

    /// <summary>Set when the billing provider could not be reached or is mis-seeded.</summary>
    public string? ErrorMessage { get; private set; }

    public async Task OnGet(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostSubscribe(string planHandle, CancellationToken cancellationToken)
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            await LoadAsync(cancellationToken);
            ErrorMessage = "Choose a plan to subscribe to.";
            return Page();
        }

        try
        {
            var subscription = await _subscriptionService.SubscribeAsync(User.Identity.Name, planHandle,
                cancellationToken);

            TempData["SubscriptionMessage"] =
                $"You are subscribed to {subscription.Plan.Name} at {subscription.Plan.BillingDescription}.";

            return RedirectToPage("./Mine");
        }
        catch (BillingConfigurationException ex)
        {
            await LoadAsync(cancellationToken);
            ErrorMessage = ex.Message;
            return Page();
        }
        catch (BillingProviderException ex)
        {
            await LoadAsync(cancellationToken);
            ErrorMessage = $"We could not complete your subscription: {ex.ProviderMessage}";
            return Page();
        }
    }

    /// <summary>
    /// Loads the page's data, degrading to a friendly message rather than an error page when the
    /// billing provider is unavailable — the rest of the storefront is unaffected either way.
    /// </summary>
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            Plans = await _subscriptionService.GetAvailablePlansAsync(cancellationToken);

            if (User?.Identity?.Name is { Length: > 0 } userReference)
            {
                CurrentSubscription = await _subscriptionService.GetCurrentSubscriptionAsync(userReference,
                    cancellationToken);
            }
        }
        catch (BillingConfigurationException ex)
        {
            ErrorMessage = $"Subscription plans are unavailable: {ex.Message}";
        }
        catch (BillingProviderException ex)
        {
            ErrorMessage = $"Subscription plans are temporarily unavailable: {ex.ProviderMessage}";
        }
    }
}
