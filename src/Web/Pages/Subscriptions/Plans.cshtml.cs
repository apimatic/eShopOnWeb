using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>UC1: browse available plans and subscribe. Mirrors Pages/Basket/Index (Razor Page, cookie auth).</summary>
[Authorize]
public class PlansModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public PlansModel(ISubscriptionService subscriptionService, UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    public IReadOnlyList<SubscriptionPlanDto> Plans { get; private set; } = Array.Empty<SubscriptionPlanDto>();
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync()
    {
        await LoadPlansAsync();
    }

    public async Task<IActionResult> OnPostSubscribeAsync(string productHandle)
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        var userReference = User.Identity!.Name!;

        var user = await _userManager.FindByNameAsync(userReference);
        var email = user?.Email ?? userReference;
        var (firstName, lastName) = SplitDisplayName(email);

        try
        {
            await _subscriptionService.SubscribeAsync(userReference, email, firstName, lastName, productHandle);
            return RedirectToPage("Mine");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await LoadPlansAsync();
            return Page();
        }
    }

    private async Task LoadPlansAsync()
    {
        try
        {
            Plans = await _subscriptionService.ListPlansAsync();
        }
        catch (Exception)
        {
            // UC1 failure scenario: plans cannot be listed (provider unreachable, bad credentials) —
            // show a friendly error; no enrollment is attempted.
            ErrorMessage = "Plans are temporarily unavailable. Please try again shortly.";
        }
    }

    private static (string FirstName, string LastName) SplitDisplayName(string email)
    {
        var localPart = email.Split('@')[0];
        var parts = localPart.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var firstName = parts.Length > 0 ? parts[0] : "eShopOnWeb";
        var lastName = parts.Length > 1 ? parts[1] : "Customer";
        return (firstName, lastName);
    }
}
