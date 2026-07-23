using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// UC1 — browse the available plans and subscribe to one. Browsing is anonymous so shoppers can see
/// pricing; subscribing requires a signed-in customer.
/// </summary>
public class PlansModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;

    public PlansModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public IReadOnlyCollection<SubscriptionPlan> Plans { get; set; } = Array.Empty<SubscriptionPlan>();

    public string? ErrorMessage { get; set; }

    public async Task OnGet()
    {
        await LoadPlansAsync();
    }

    [Authorize]
    public async Task<IActionResult> OnPostSubscribe(string planHandle)
    {
        Guard.Against.Null(User.Identity, nameof(User.Identity));
        Guard.Against.NullOrEmpty(User.Identity!.Name, nameof(User.Identity.Name));

        try
        {
            var subscription = await _subscriptionService.SubscribeAsync(User.Identity.Name!, planHandle);

            return RedirectToPage("./Mine", new { subscribed = subscription.Id });
        }
        catch (Exception ex) when (ex is BillingProviderException or InvalidSubscriptionOperationException)
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
        catch (BillingProviderException ex)
        {
            // Plans could not be listed — show a friendly error and attempt no enrollment.
            ErrorMessage = $"Subscription plans are unavailable right now. {ex.Message}";
            Plans = Array.Empty<SubscriptionPlan>();
        }
    }
}
