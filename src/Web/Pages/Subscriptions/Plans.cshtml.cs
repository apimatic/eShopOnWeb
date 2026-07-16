using System.Collections.Generic;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>UC1 step 1-2: browse available plans and subscribe. Mirrors Pages/Basket/Index.</summary>
[Authorize]
public class PlansModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAppLogger<PlansModel> _logger;

    public PlansModel(ISubscriptionService subscriptionService, UserManager<ApplicationUser> userManager, IAppLogger<PlansModel> logger)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
        _logger = logger;
    }

    public IReadOnlyList<BillingPlan> Plans { get; set; } = new List<BillingPlan>();
    public string? LoadError { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        try
        {
            Plans = await _subscriptionService.ListPlansAsync();
        }
        catch (BillingProviderException ex)
        {
            // Plans cannot be listed (provider unreachable, bad credentials) → show a friendly error;
            // no enrollment is attempted (UC1 failure scenarios).
            _logger.LogWarning("Failed to list plans: {Message}", ex.Message);
            LoadError = "We couldn't load the available plans right now. Please try again shortly.";
        }
    }

    public async Task<IActionResult> OnPostSubscribeAsync(string planHandle)
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        var username = User.Identity.Name!;

        try
        {
            var user = await _userManager.GetUserAsync(User);
            await _subscriptionService.SubscribeAsync(username, user?.Email ?? username, string.Empty, string.Empty, planHandle);
            StatusMessage = "You're subscribed! Here's your account.";
            return RedirectToPage("./Mine");
        }
        catch (InvalidSubscriptionStateException ex)
        {
            StatusMessage = ex.Message;
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Subscribe failed for {Username}: {Message}", username, ex.Message);
            StatusMessage = "We couldn't complete your subscription. Please try again.";
        }

        return RedirectToPage();
    }
}
