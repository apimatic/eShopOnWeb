using System.Collections.Generic;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>UC1 — lists available plans and lets the customer subscribe (mirror Pages/Basket/Index).</summary>
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

    public IReadOnlyList<BillingPlan> Plans { get; private set; } = new List<BillingPlan>();

    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync()
    {
        await LoadPlansAsync();
    }

    public async Task<IActionResult> OnPostSubscribeAsync(string productHandle)
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        var userReference = User.Identity!.Name!;

        try
        {
            await _subscriptionService.SubscribeAsync(userReference, userReference, string.Empty, string.Empty, productHandle);
        }
        catch (BillingConfigurationException ex)
        {
            _logger.LogWarning("Subscribe failed for {0}: {1}", userReference, ex.Message);
            await LoadPlansAsync();
            ErrorMessage = "This plan is temporarily unavailable. Please try again later.";
            return Page();
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Subscribe failed for {0}: {1}", userReference, ex.Message);
            await LoadPlansAsync();
            ErrorMessage = "We could not complete your subscription right now. Please try again.";
            return Page();
        }

        return RedirectToPage("Mine");
    }

    private async Task LoadPlansAsync()
    {
        try
        {
            Plans = await _subscriptionService.ListPlansAsync();
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Failed to list plans: {0}", ex.Message);
            Plans = new List<BillingPlan>();
            ErrorMessage = "Plans are temporarily unavailable. Please try again later.";
        }
    }
}
