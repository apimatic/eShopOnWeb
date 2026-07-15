using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

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

    public IReadOnlyList<SubscriptionPlan> Plans { get; set; } = Array.Empty<SubscriptionPlan>();
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        try
        {
            Plans = await _subscriptionService.GetAvailablePlansAsync();
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Failed to list subscription plans: {0}", ex.Message);
            ErrorMessage = "We couldn't load subscription plans right now. Please try again shortly.";
        }
    }

    public async Task<IActionResult> OnPostSubscribeAsync(string planHandle)
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));

        try
        {
            var subscription = await _subscriptionService.SubscribeAsync(User.Identity.Name, User.Identity.Name,
                planHandle);
            TempData["SubscriptionMessage"] =
                $"You're subscribed to {subscription.PlanName} (state: {subscription.State}).";
        }
        catch (BillingProviderException ex)
        {
            TempData["SubscriptionError"] = ex.Message;
        }

        return RedirectToPage("Mine");
    }
}
