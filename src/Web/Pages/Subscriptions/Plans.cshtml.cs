using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// Browse the available plans and subscribe (UC1). Browsing is anonymous; subscribing requires the
/// signed-in customer, exactly like the rest of the storefront.
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

    /// <summary>Set when the plans could not be listed, so the page can explain itself (UC1 failure scenario).</summary>
    public string? ErrorMessage { get; private set; }

    public async Task OnGet()
    {
        await LoadPlansAsync();
    }

    public async Task<IActionResult> OnPost(string planHandle)
    {
        var userName = User.Identity?.Name;
        if (string.IsNullOrEmpty(userName))
        {
            return RedirectToPage("/Account/Login", new { area = "Identity" });
        }

        try
        {
            var subscription = await _subscriptionService.SubscribeAsync(userName, planHandle);
            return RedirectToPage("./Mine", new { subscribed = subscription.Id });
        }
        catch (ActiveSubscriptionExistsException ex)
        {
            // Already subscribed on another plan — the customer wants a plan change, not a second
            // enrolment, so send them to the page that offers it.
            _logger.LogWarning(ex.Message);
            TempData["SubscriptionMessage"] = ex.Message;
            return RedirectToPage("./Mine");
        }
        catch (Exception ex) when (ex is BillingProviderException or PlanNotFoundException or BillingConfigurationException)
        {
            _logger.LogWarning("Subscribing {0} to {1} failed: {2}", userName, planHandle, ex.Message);
            ErrorMessage = ex.Message;
            await LoadPlansAsync();
            return Page();
        }
    }

    private async Task LoadPlansAsync()
    {
        try
        {
            Plans = await _subscriptionService.ListPlansAsync();
        }
        catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException)
        {
            _logger.LogWarning("Listing plans failed: {0}", ex.Message);
            ErrorMessage = "Subscription plans are unavailable right now. Please try again shortly.";
        }
    }
}
