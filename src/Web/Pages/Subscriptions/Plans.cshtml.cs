using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// The plan catalog and the hero "Subscribe" action (plan.md UC1).
/// </summary>
/// <remarks>
/// Browsing is anonymous so shoppers can compare plans before signing in; subscribing challenges
/// for a login, because the enrolment has to be attached to a real eShopOnWeb identity.
/// </remarks>
public class PlansModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly IAppLogger<PlansModel> _logger;

    public PlansModel(ISubscriptionService subscriptionService, IAppLogger<PlansModel> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    public IReadOnlyList<BillingPlan> Plans { get; private set; } = Array.Empty<BillingPlan>();

    /// <summary>The plans the signed-in customer is already enrolled in, by handle.</summary>
    public IReadOnlyCollection<string> SubscribedPlanHandles { get; private set; } = Array.Empty<string>();

    /// <summary>Set when the plans could not be listed; the page stays usable and explains why.</summary>
    public string? ErrorMessage { get; private set; }

    public async Task OnGet(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostSubscribe(string planHandle, CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            // Send the shopper through login and back to the plans page.
            return Challenge();
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
                User.Identity.Name!, planHandle, cancellationToken);

            TempData["SubscriptionMessage"] =
                $"You are subscribed to {subscription.Plan.Name} at {subscription.Plan.PriceDescription}.";

            return RedirectToPage("./Mine");
        }
        catch (BillingConfigurationException ex)
        {
            _logger.LogWarning("Subscribe failed because of a configuration problem: {0}", ex.Message);
            ErrorMessage = "This plan is not available at the moment. Please try again later.";
        }
        catch (BillingProviderException ex)
        {
            // Surface the provider's own message: it is what tells the customer why enrolment
            // was refused (UC1 failure scenarios).
            _logger.LogWarning("Subscribe failed for {0}: {1}", User.Identity.Name, ex.Message);
            ErrorMessage = ex.Message;
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            Plans = await _subscriptionService.ListPlansAsync(cancellationToken);
        }
        catch (BillingProviderException ex)
        {
            // No enrolment is attempted when the catalog cannot be read (UC1 failure scenarios).
            _logger.LogWarning("Could not list subscription plans: {0}", ex.Message);
            Plans = Array.Empty<BillingPlan>();
            ErrorMessage ??= "Subscription plans are temporarily unavailable. Please try again shortly.";
            return;
        }

        if (User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        try
        {
            var mine = await _subscriptionService.ListSubscriptionsForUserAsync(
                User.Identity.Name!, cancellationToken);

            SubscribedPlanHandles = mine
                .Where(s => s.IsActive)
                .Select(s => s.Plan.Handle)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (BillingProviderException ex)
        {
            // Knowing which plan the customer is already on is a nicety, not a requirement.
            _logger.LogWarning("Could not read existing subscriptions for {0}: {1}",
                User.Identity.Name, ex.Message);
        }
    }
}
