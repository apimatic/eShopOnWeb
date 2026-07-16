using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Web.ViewModels;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>UC1: browse plans and subscribe. Mirrors <c>Pages/Basket/Index</c>.</summary>
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
    public string? ErrorMessage { get; set; }

    public async Task OnGet()
    {
        await LoadPlansAsync();
    }

    public async Task<IActionResult> OnPostSubscribeAsync(string productHandle)
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        var userId = User.Identity.Name!;

        try
        {
            await _subscriptionService.SubscribeAsync(userId, userId, productHandle);
        }
        catch (BillingProviderException ex)
        {
            // UC1 failure scenarios: surface a friendly error; no partial state to roll back since
            // enrollment either fully succeeded or didn't happen.
            _logger.LogWarning("Subscribe failed for user {0} on plan {1}: {2}", userId, productHandle, ex.Message);
            await LoadPlansAsync();
            ErrorMessage = "We couldn't complete your subscription right now. Please try again shortly.";
            return Page();
        }

        return RedirectToPage("/Subscriptions/Mine");
    }

    private async Task LoadPlansAsync()
    {
        try
        {
            var plans = await _subscriptionService.ListPlansAsync();
            Plans = plans.Select(p => new PlanViewModel
            {
                Handle = p.Handle,
                Name = p.Name,
                PriceInCents = p.PriceInCents,
                IntervalUnit = p.IntervalUnit.ToString()
            }).ToList();
        }
        catch (BillingProviderException ex)
        {
            // UC1 failure scenario: plans cannot be listed (provider unreachable, bad credentials) ->
            // show a friendly error; no enrollment is attempted.
            _logger.LogWarning("Unable to list plans: {0}", ex.Message);
            ErrorMessage = "Plans are temporarily unavailable. Please try again shortly.";
        }
    }
}
