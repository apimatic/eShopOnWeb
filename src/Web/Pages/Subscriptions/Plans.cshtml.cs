using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// The plans catalog and the subscribe action (UC1). Browsing is open to everyone; subscribing requires
/// a signed-in shopper.
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

    public IReadOnlyCollection<SubscriptionPlan> Plans { get; private set; } = Array.Empty<SubscriptionPlan>();

    /// <summary>Handles of the plans the signed-in shopper is already subscribed to.</summary>
    public IReadOnlyCollection<string> SubscribedPlanHandles { get; private set; } = Array.Empty<string>();

    public string? ErrorMessage { get; private set; }

    public async Task OnGet(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    [Authorize]
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
            var subscription = await _subscriptionService.SubscribeAsync(User.Identity.Name, planHandle, cancellationToken);

            return RedirectToPage("./Mine", new { highlight = subscription.Id });
        }
        catch (BillingConfigurationException ex)
        {
            _logger.LogWarning("Subscribe to {PlanHandle} failed because of a catalog configuration problem: {Reason}", planHandle, ex.Message);
            await LoadAsync(cancellationToken);
            ErrorMessage = "That plan is not available at the moment. Please try again later.";
            return Page();
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Subscribe to {PlanHandle} was rejected by the billing provider: {Reason}", planHandle, ex.Message);
            await LoadAsync(cancellationToken);
            ErrorMessage = $"We could not complete your subscription: {ex.ProviderMessage}";
            return Page();
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            Plans = await _subscriptionService.GetPlansAsync(cancellationToken);

            if (User?.Identity?.IsAuthenticated == true && User.Identity.Name is not null)
            {
                var mine = await _subscriptionService.GetSubscriptionsAsync(User.Identity.Name, cancellationToken);

                SubscribedPlanHandles = mine.Where(subscription => subscription.IsActive && subscription.PlanHandle is not null)
                    .Select(subscription => subscription.PlanHandle!)
                    .ToList();
            }
        }
        catch (BillingConfigurationException ex)
        {
            _logger.LogWarning("Subscription plans could not be listed because of a catalog configuration problem: {Reason}", ex.Message);
            ErrorMessage = "Subscription plans are unavailable right now. Please check back shortly.";
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Subscription plans could not be listed: {Reason}", ex.Message);
            ErrorMessage = "Subscription plans are unavailable right now. Please check back shortly.";
        }
    }
}
