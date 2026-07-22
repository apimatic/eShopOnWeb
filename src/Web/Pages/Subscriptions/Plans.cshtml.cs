using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// The storefront's plan catalogue and the hero subscribe action (UC1).
/// </summary>
[Authorize]
public class PlansModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;

    public PlansModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public IReadOnlyCollection<BillingPlan> Plans { get; private set; } = Array.Empty<BillingPlan>();

    public string? ErrorMessage { get; private set; }

    public async Task OnGet()
    {
        await LoadPlansAsync();
    }

    public async Task<IActionResult> OnPostAsync(string planHandle)
    {
        try
        {
            await _subscriptionService.SubscribeAsync(UserReference(), planHandle);
            return RedirectToPage("./Mine");
        }
        catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException)
        {
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
            // No enrollment is attempted when the catalogue cannot be read.
            ErrorMessage = "Subscription plans are unavailable right now. Please try again shortly.";
            Plans = Array.Empty<BillingPlan>();
        }
    }

    private string UserReference()
    {
        Guard.Against.Null(User.Identity, nameof(User.Identity));
        Guard.Against.NullOrWhiteSpace(User.Identity.Name, nameof(User.Identity.Name));

        return User.Identity.Name!;
    }
}
