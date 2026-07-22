using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// UC1 steps 1–2 — browse the available plans and subscribe to one.
/// </summary>
[Authorize]
public class PlansModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;

    public PlansModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public IReadOnlyCollection<SubscriptionPlan> Plans { get; set; } = Array.Empty<SubscriptionPlan>();

    public Subscription? CurrentSubscription { get; set; }

    public string? ErrorMessage { get; set; }

    public async Task OnGet()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostSubscribe(string planHandle)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            ModelState.AddModelError(string.Empty, "Choose a plan to subscribe to.");
            await LoadAsync();

            return Page();
        }

        try
        {
            await _subscriptionService.SubscribeAsync(User.Identity!.Name!, planHandle);
        }
        catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadAsync();

            return Page();
        }

        return RedirectToPage("./Mine");
    }

    private async Task LoadAsync()
    {
        try
        {
            Plans = await _subscriptionService.GetAvailablePlansAsync();
            CurrentSubscription = await _subscriptionService.GetActiveSubscriptionForUserAsync(User.Identity!.Name!);
        }
        catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException)
        {
            // Show a friendly error rather than a stack trace; no enrolment is attempted (UC1).
            ErrorMessage = ex.Message;
        }
    }
}
