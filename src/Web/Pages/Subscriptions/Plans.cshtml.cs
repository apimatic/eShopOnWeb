using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

public class PlansModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public PlansModel(ISubscriptionService subscriptionService, UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    public IReadOnlyList<BillingPlan> Plans { get; set; } = Array.Empty<BillingPlan>();

    [TempData]
    public string? ConfirmationMessage { get; set; }

    public string? ErrorMessage { get; set; }

    public async Task OnGet()
    {
        await LoadPlansAsync();
    }

    public async Task<IActionResult> OnPostSubscribeAsync(string planHandle)
    {
        if (User?.Identity?.IsAuthenticated != true)
        {
            return Challenge();
        }

        var identityUser = await _userManager.GetUserAsync(User);
        Guard.Against.Null(identityUser, nameof(identityUser));
        Guard.Against.NullOrEmpty(identityUser.UserName, nameof(identityUser.UserName));

        try
        {
            var result = await _subscriptionService.SubscribeAsync(
                identityUser.UserName,
                identityUser.Email ?? $"{identityUser.UserName}@eshoponweb.local",
                identityUser.UserName,
                identityUser.UserName,
                planHandle);

            ConfirmationMessage = result.WasAlreadyEnrolled
                ? $"You're already subscribed to {result.Subscription.ProductName}."
                : $"Subscribed to {result.Subscription.ProductName} - $ {result.Subscription.PriceInCents / 100m:N2} - next billing date {result.Subscription.CurrentPeriodEndsAt:d}.";

            return RedirectToPage("/Subscriptions/Mine");
        }
        catch (BillingConfigurationException)
        {
            ErrorMessage = "This plan is temporarily unavailable. Please contact support.";
        }
        catch (BillingProviderException ex)
        {
            ErrorMessage = $"We couldn't complete your subscription: {ex.Message}";
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
        catch (BillingProviderException)
        {
            ErrorMessage = "Plans are temporarily unavailable. Please try again later.";
        }
    }
}
