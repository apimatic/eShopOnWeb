using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// UC1 — browse the available plans and subscribe to one.
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

    public async Task<IActionResult> OnPostSubscribe(string planHandle)
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));

        try
        {
            await _subscriptionService.SubscribeAsync(User.Identity.Name!, planHandle);
            return RedirectToPage("./Mine");
        }
        catch (BillingProviderException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (BillingConfigurationException ex)
        {
            ErrorMessage = ex.Message;
        }

        await LoadPlansAsync();
        return Page();
    }

    private async Task LoadPlansAsync()
    {
        try
        {
            Plans = await _subscriptionService.GetAvailablePlansAsync();
        }
        catch (BillingProviderException ex)
        {
            // No enrollment is attempted when plans cannot be listed (UC1 failure scenarios).
            ErrorMessage = $"Plans are unavailable right now. {ex.Message}";
        }
        catch (BillingConfigurationException ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
