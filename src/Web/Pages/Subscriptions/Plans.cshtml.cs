using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// Browse the available plans and subscribe to one (UC1).
/// </summary>
[Authorize]
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

    /// <summary>The plan the customer is already on, so the page can mark it as current.</summary>
    public string? CurrentPlanHandle { get; private set; }

    public string? ErrorMessage { get; private set; }

    public async Task OnGet()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostSubscribe(string planHandle)
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            await LoadAsync();
            ErrorMessage = "Choose a plan to subscribe to.";
            return Page();
        }

        try
        {
            await _subscriptionService.SubscribeAsync(User.Identity.Name, planHandle);
        }
        catch (BillingConfigurationException configurationException)
        {
            _logger.LogWarning(configurationException.Message);
            await LoadAsync();
            ErrorMessage = "That plan is not available right now. Please contact support.";
            return Page();
        }
        catch (BillingProviderException providerException)
        {
            _logger.LogWarning(providerException.Message);
            await LoadAsync();
            ErrorMessage = "We could not complete your subscription. Please try again shortly.";
            return Page();
        }

        return RedirectToPage("./Mine");
    }

    private async Task LoadAsync()
    {
        try
        {
            Plans = await _subscriptionService.GetAvailablePlansAsync();
        }
        catch (BillingProviderException providerException)
        {
            // The plans could not be listed, so no enrollment is attempted (UC1 failure path).
            _logger.LogWarning($"Could not list subscription plans: {providerException.Message}");
            Plans = Array.Empty<BillingPlan>();
            ErrorMessage = "Subscription plans are unavailable right now. Please try again shortly.";
            return;
        }

        CurrentPlanHandle = await FindCurrentPlanHandleAsync();
    }

    private async Task<string?> FindCurrentPlanHandleAsync()
    {
        if (string.IsNullOrEmpty(User?.Identity?.Name))
        {
            return null;
        }

        try
        {
            var subscriptions = await _subscriptionService.GetSubscriptionsForUserAsync(User.Identity.Name);

            return subscriptions.FirstOrDefault(subscription => subscription.IsLive)?.PlanHandle;
        }
        catch (BillingProviderException providerException)
        {
            // Not knowing the current plan must not stop the plans themselves from rendering.
            _logger.LogWarning($"Could not read the current subscription: {providerException.Message}");
            return null;
        }
    }
}
