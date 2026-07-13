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
    private readonly IAppLogger<PlansModel> _logger;

    public PlansModel(ISubscriptionService subscriptionService, IAppLogger<PlansModel> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    public IReadOnlyList<BillingPlan> Plans { get; set; } = Array.Empty<BillingPlan>();

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGet()
    {
        try
        {
            Plans = await _subscriptionService.ListPlansAsync();
        }
        catch (BillingProviderException ex)
        {
            // Plans could not be listed (provider unreachable, bad credentials) -> friendly error;
            // no enrollment is attempted (§ UC1 failure scenarios).
            _logger.LogWarning("Could not list subscription plans: {0}", ex.Message);
            ErrorMessage = "Plans are temporarily unavailable. Please try again shortly.";
        }
    }

    public async Task<IActionResult> OnPostSubscribeAsync(string productHandle)
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        var userReference = User.Identity!.Name!;

        try
        {
            await _subscriptionService.SubscribeAsync(userReference, userReference, productHandle);
        }
        catch (Exception ex) when (ex is BillingProviderException or ArgumentException)
        {
            _logger.LogWarning("Subscribe failed for {0} on plan {1}: {2}", userReference, productHandle, ex.Message);
            ErrorMessage = "We couldn't complete your subscription. Please try again shortly.";
            return RedirectToPage();
        }

        return RedirectToPage("./Mine");
    }
}
