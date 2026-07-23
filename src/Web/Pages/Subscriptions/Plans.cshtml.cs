using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// The subscription catalog (UC1, steps 1-2): browse the available plans and subscribe to one.
/// </summary>
public class PlansModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;

    public PlansModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public IReadOnlyCollection<SubscriptionPlan> Plans { get; private set; } = Array.Empty<SubscriptionPlan>();

    /// <summary>The user's current subscription, so a plan they are already on is not offered again.</summary>
    public Subscription? CurrentSubscription { get; private set; }

    public string? ErrorMessage { get; private set; }

    public async Task OnGet()
    {
        await LoadAsync();
    }

    [Authorize]
    public async Task<IActionResult> OnPostSubscribe(string planHandle)
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));

        try
        {
            await _subscriptionService.SubscribeAsync(User.Identity.Name!, planHandle);
            return RedirectToPage("./Mine");
        }
        catch (Exception ex) when (ex is BillingProviderException or InvalidSubscriptionOperationException)
        {
            // Show the provider's own message rather than a stack trace; nothing was enrolled.
            ErrorMessage = ex.Message;
            await LoadAsync();
            return Page();
        }
    }

    private async Task LoadAsync()
    {
        try
        {
            Plans = await _subscriptionService.ListPlansAsync();

            if (User?.Identity?.IsAuthenticated == true && User.Identity.Name is not null)
            {
                CurrentSubscription = await _subscriptionService.GetLiveSubscriptionForUserAsync(User.Identity.Name);
            }
        }
        catch (BillingProviderException ex)
        {
            // Plans could not be listed — the page stays usable and no enrollment is attempted.
            ErrorMessage ??= ex.Message;
            Plans = Array.Empty<SubscriptionPlan>();
        }
    }
}
