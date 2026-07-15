using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Web.ViewModels;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>UC1: browse available plans and subscribe. Mirrors Pages/Basket/Index.</summary>
public class PlansModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;

    public PlansModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public List<SubscriptionPlanViewModel> Plans { get; private set; } = new();
    public string? ErrorMessage { get; private set; }
    public string? ConfirmationMessage { get; private set; }

    public async Task OnGetAsync()
    {
        await LoadPlansAsync();
    }

    public async Task<IActionResult> OnPostSubscribeAsync(string planHandle)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return RedirectToPage("/Account/Login", new { area = "Identity", returnUrl = "/Subscriptions/Plans" });
        }

        Guard.Against.Null(User.Identity.Name, nameof(User.Identity.Name));
        Guard.Against.NullOrEmpty(planHandle, nameof(planHandle));

        try
        {
            var subscription = await _subscriptionService.SubscribeAsync(User.Identity.Name, User.Identity.Name, planHandle);
            ConfirmationMessage = $"You're subscribed to {subscription.PlanName} ({subscription.State}). Next billing date: {(subscription.NextBillingDate?.ToString("d") ?? "n/a")}.";
        }
        catch (BillingProviderException)
        {
            ErrorMessage = "We couldn't complete your subscription right now. Please try again later.";
        }

        await LoadPlansAsync();
        return Page();
    }

    private async Task LoadPlansAsync()
    {
        try
        {
            var plans = await _subscriptionService.ListPlansAsync();
            Plans = plans.Select(p => new SubscriptionPlanViewModel
            {
                Handle = p.Handle,
                Name = p.Name,
                Price = p.PriceInCents / 100m,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit
            }).ToList();
        }
        catch (BillingProviderException)
        {
            // Plans cannot be listed (provider unreachable/misconfigured) → friendly error, no enrollment attempted.
            ErrorMessage = "We couldn't load subscription plans right now. Please try again later.";
        }
    }
}
