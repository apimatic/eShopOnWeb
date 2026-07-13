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

    public PlansModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public IReadOnlyList<SubscriptionPlan> Plans { get; set; } = Array.Empty<SubscriptionPlan>();
    public SubscriptionDetails? ActiveSubscription { get; set; }
    public string? ErrorMessage { get; set; }

    public async Task OnGet()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostSubscribe(string productHandle)
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        var customerReference = User.Identity.Name!;

        try
        {
            await _subscriptionService.SubscribeAsync(customerReference, customerReference, productHandle);
        }
        catch (BillingProviderException)
        {
            // Configured product handle does not resolve, or the provider rejected enrollment:
            // fail with a configuration/provider error rather than a guessed plan (UC1 failure scenarios).
            await LoadAsync();
            ErrorMessage = "We couldn't complete your subscription right now. Please try again shortly.";
            return Page();
        }

        return RedirectToPage("Mine");
    }

    private async Task LoadAsync()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        var customerReference = User.Identity.Name!;

        try
        {
            Plans = await _subscriptionService.ListPlansAsync();
            ActiveSubscription = await _subscriptionService.GetActiveSubscriptionAsync(customerReference);
        }
        catch (BillingProviderException)
        {
            // Provider unreachable or bad credentials: show a friendly error, attempt no enrollment (UC1 failure scenario).
            ErrorMessage = "Plans are temporarily unavailable. Please try again shortly.";
        }
    }
}
