using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
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

    public IReadOnlyList<BillingPlan> Plans { get; set; } = Array.Empty<BillingPlan>();
    public string? CurrentPlanHandle { get; set; }
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostSubscribeAsync(string productHandle)
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));

        try
        {
            await _subscriptionService.SubscribeAsync(User.Identity!.Name!, productHandle);
            return RedirectToPage("/Subscriptions/Mine");
        }
        catch (BillingProviderException ex)
        {
            ErrorMessage = ex.Message;
        }

        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        try
        {
            Plans = await _subscriptionService.GetAvailablePlansAsync();
        }
        catch (BillingProviderException ex)
        {
            ErrorMessage = ex.Message;
            return;
        }

        if (User?.Identity?.Name != null)
        {
            var existing = await _subscriptionService.GetMySubscriptionAsync(User.Identity.Name);
            CurrentPlanHandle = existing?.ProductHandle;
        }
    }
}
