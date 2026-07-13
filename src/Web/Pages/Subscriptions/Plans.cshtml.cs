using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Web.ViewModels.Subscriptions;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>UC1 (hero): browse available plans and subscribe. Mirror <c>Pages/Basket/Index</c>.</summary>
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

    public List<PlanViewModel> Plans { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        await LoadPlansAsync();
    }

    public async Task<IActionResult> OnPostSubscribeAsync(string productHandle, string firstName, string lastName)
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        var username = User.Identity!.Name!;

        try
        {
            var subscription = await _subscriptionService.SubscribeAsync(username, username, firstName, lastName, productHandle);
            StatusMessage = $"You are subscribed to {subscription.ProductName}.";
            return RedirectToPage("Mine");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Subscribe failed for {0} to plan {1}: {2}", username, productHandle, ex.Message);
            StatusMessage = $"Could not subscribe: {ex.Message}";
            await LoadPlansAsync();
            return Page();
        }
    }

    private async Task LoadPlansAsync()
    {
        var plans = await _subscriptionService.ListPlansAsync();
        Plans = plans.Select(p => new PlanViewModel
        {
            Id = p.Id,
            Handle = p.Handle,
            Name = p.Name,
            Price = p.PriceInCents / 100m,
            RequiresPaymentMethod = p.RequiresPaymentMethod
        }).ToList();
    }
}
