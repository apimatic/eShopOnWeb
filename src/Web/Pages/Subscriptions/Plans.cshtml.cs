using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// The storefront plan catalogue and the hero subscribe action (UC1).
/// </summary>
public class PlansModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly IAppLogger<PlansModel> _logger;

    public PlansModel(ISubscriptionService subscriptionService, IAppLogger<PlansModel> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    public IReadOnlyCollection<BillingPlan> Plans { get; private set; } = Array.Empty<BillingPlan>();

    /// <summary>The handles the signed-in shopper is already subscribed to, so the page can say so.</summary>
    public IReadOnlyCollection<string> SubscribedPlanHandles { get; private set; } = Array.Empty<string>();

    /// <summary>A friendly message shown when the plans cannot be listed at all.</summary>
    public string? ErrorMessage { get; private set; }

    public async Task OnGet(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    [Authorize]
    public async Task<IActionResult> OnPostSubscribe(string planHandle, CancellationToken cancellationToken)
    {
        var userName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Challenge();
        }

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            await LoadAsync(cancellationToken);
            ErrorMessage = "Choose a plan to subscribe to.";
            return Page();
        }

        try
        {
            var subscription = await _subscriptionService.SubscribeAsync(
                SubscriptionActor.Customer(userName),
                planHandle,
                cancellationToken);

            return RedirectToPage("./Mine", new { highlight = subscription.Id });
        }
        catch (BillingConfigurationException exception)
        {
            _logger.LogWarning("Subscribe to '{0}' failed on configuration: {1}", planHandle, exception.Message);
            ErrorMessage = "That plan is not available right now. Please try another plan.";
        }
        catch (BillingProviderException exception)
        {
            _logger.LogWarning("Subscribe to '{0}' was rejected: {1}", planHandle, exception.Message);
            ErrorMessage = exception.DisplayMessage;
        }

        await LoadAsync(cancellationToken);

        return Page();
    }

    /// <summary>
    /// Loads the catalogue. A provider outage degrades the page to a friendly message rather than
    /// an error screen, and no enrollment is attempted.
    /// </summary>
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            Plans = await _subscriptionService.GetAvailablePlansAsync(cancellationToken);
        }
        catch (Exception exception) when (
            exception is BillingProviderException or BillingConfigurationException)
        {
            _logger.LogWarning("Could not list subscription plans: {0}", exception.Message);
            ErrorMessage ??= "Subscription plans are temporarily unavailable. Please try again shortly.";
            Plans = Array.Empty<BillingPlan>();

            return;
        }

        var userName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return;
        }

        try
        {
            var subscriptions = await _subscriptionService.GetSubscriptionsAsync(userName, cancellationToken);
            SubscribedPlanHandles = subscriptions
                .Where(subscription => subscription.IsLive)
                .Select(subscription => subscription.PlanHandle)
                .ToArray();
        }
        catch (BillingProviderException exception)
        {
            // The catalogue is still perfectly usable without the "you are subscribed" markers.
            _logger.LogWarning("Could not read existing subscriptions for {0}: {1}", userName, exception.Message);
        }
    }
}
