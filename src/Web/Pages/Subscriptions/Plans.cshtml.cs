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
    public string? ActiveProductHandle { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGet()
    {
        await LoadPlansAsync();

        Guard.Against.NullOrEmpty(User?.Identity?.Name, nameof(User.Identity.Name));
        var mine = await _subscriptionService.FindSubscriptionForUserAsync(User.Identity.Name);
        ActiveProductHandle = mine?.ProductHandle;
    }

    public async Task<IActionResult> OnPostSubscribeAsync(string productHandle)
    {
        Guard.Against.NullOrEmpty(User?.Identity?.Name, nameof(User.Identity.Name));

        try
        {
            await _subscriptionService.SubscribeAsync(User.Identity.Name, User.Identity.Name, productHandle);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Failed to subscribe {UserName} to {ProductHandle}: {Message}", User.Identity.Name, productHandle, ex.Message);
            ErrorMessage = "We couldn't complete your subscription right now. Please try again shortly.";
            return RedirectToPage();
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
            _logger.LogWarning("Failed to load plans: {Message}", ex.Message);
            ErrorMessage = "We couldn't load the available plans right now. Please try again shortly.";
        }
    }
}
