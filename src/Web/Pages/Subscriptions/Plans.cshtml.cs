using System;
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

/// <summary>UC1 step 1-2 — browse plans and subscribe. Mirrors <c>Pages/Basket/Index</c>.</summary>
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

    public IReadOnlyList<BillingPlan> Plans { get; set; } = Array.Empty<BillingPlan>();
    public string? ErrorMessage { get; set; }

    public async Task OnGet()
    {
        await LoadPlansAsync();
    }

    public async Task<IActionResult> OnPostSubscribeAsync(string planHandle)
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        var userName = User.Identity!.Name!;

        try
        {
            await _subscriptionService.SubscribeAsync(userName, userName, userName, planHandle);
        }
        catch (BillingConfigurationException ex)
        {
            _logger.LogWarning("Subscribe failed for {0} (configuration): {1}", userName, ex.Message);
            ErrorMessage = ex.Message;
            await LoadPlansAsync();
            return Page();
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Subscribe failed for {0} (provider): {1}", userName, ex.Message);
            ErrorMessage = "The billing provider rejected the request: " + ex.Message;
            await LoadPlansAsync();
            return Page();
        }

        return RedirectToPage("./Mine");
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
            ErrorMessage ??= "Plans are temporarily unavailable — please try again later.";
        }
    }
}
