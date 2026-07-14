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
    private readonly IAppLogger<PlansModel> _logger;

    public PlansModel(ISubscriptionService subscriptionService, IAppLogger<PlansModel> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    public IReadOnlyList<BillingPlan> Plans { get; set; } = Array.Empty<BillingPlan>();
    public string? ErrorMessage { get; set; }

    [TempData]
    public string? SubscribeError { get; set; }

    public async Task OnGet()
    {
        ErrorMessage = SubscribeError;

        try
        {
            Plans = await _subscriptionService.ListPlansAsync();
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Failed to list plans: {0}", ex.Message);
            ErrorMessage = "Plans are temporarily unavailable. Please try again later.";
        }
    }

    public async Task<IActionResult> OnPostSubscribeAsync(string productHandle)
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        var buyerId = User.Identity!.Name!;

        try
        {
            await _subscriptionService.SubscribeAsync(buyerId, buyerId, productHandle);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Subscribe failed for {0}: {1}", buyerId, ex.Message);
            SubscribeError = ex.Message;
            return RedirectToPage();
        }

        return RedirectToPage("./Mine");
    }
}
