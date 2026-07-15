using System.Collections.Generic;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>UC1: browse the available recurring plans and subscribe (mirrors Pages/Basket/Index).</summary>
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

    public IReadOnlyList<BillingPlan> Plans { get; set; } = new List<BillingPlan>();
    public string? ErrorMessage { get; set; }

    public async Task OnGet()
    {
        await LoadPlansAsync();
    }

    public async Task<IActionResult> OnPostSubscribeAsync(string productHandle)
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        var userId = User!.Identity!.Name!;

        try
        {
            await _subscriptionService.SubscribeAsync(userId, userId, productHandle);
            return RedirectToPage("Mine");
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (BillingConfigurationException ex)
        {
            _logger.LogWarning("Subscribe failed (configuration): {0}", ex.Message);
            ErrorMessage = "This plan is temporarily unavailable. Please try again later.";
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Subscribe failed (provider): {0}", ex.Message);
            ErrorMessage = "We couldn't complete your subscription right now. Please try again shortly.";
        }

        await LoadPlansAsync();
        return Page();
    }

    private async Task LoadPlansAsync()
    {
        try
        {
            Plans = await _subscriptionService.ListPlansAsync();
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Could not list plans: {0}", ex.Message);
            ErrorMessage = "Plans are temporarily unavailable. Please try again shortly.";
        }
    }
}
