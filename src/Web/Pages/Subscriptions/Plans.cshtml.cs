using System.Collections.Generic;
using System.Linq;
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

    public List<BillingPlan> Plans { get; set; } = new();
    public Subscription? MySubscription { get; set; }
    public string? ErrorMessage { get; set; }

    public async Task OnGet()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostSubscribeAsync(string productHandle)
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));

        try
        {
            await _subscriptionService.SubscribeAsync(User.Identity.Name!, productHandle);
            return RedirectToPage("Mine");
        }
        catch (BillingProviderException ex)
        {
            ErrorMessage = ex.Message;
            await LoadAsync();
            return Page();
        }
    }

    private async Task LoadAsync()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));

        try
        {
            var plans = await _subscriptionService.ListPlansAsync();
            Plans = plans.ToList();
            MySubscription = await _subscriptionService.GetMySubscriptionAsync(User.Identity.Name!);
        }
        catch (BillingProviderException)
        {
            ErrorMessage = "We couldn't load subscription plans right now. Please try again later.";
        }
    }
}
