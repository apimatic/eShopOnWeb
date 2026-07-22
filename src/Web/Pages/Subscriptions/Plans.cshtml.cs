using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// UC1 — browse the plans on offer and subscribe. Browsing is open to anyone; subscribing requires
/// a signed in customer, so an anonymous visitor is sent to the login page first.
/// </summary>
public class PlansModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;

    public PlansModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public IReadOnlyList<BillingPlan> Plans { get; private set; } = Array.Empty<BillingPlan>();

    /// <summary>Set when the plans could not be listed, so the page can say so politely.</summary>
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadPlansAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(string planHandle, CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return RedirectToPage("/Account/Login", new { area = "Identity", returnUrl = Url.Page("/Subscriptions/Plans") });
        }

        try
        {
            var actor = new SubscriptionActor(User.Identity.Name ?? string.Empty, User.IsInRole("Administrators"));
            await _subscriptionService.SubscribeAsync(actor, planHandle, cancellationToken);

            return RedirectToPage("./Mine");
        }
        catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException
            or InvalidBillingRequestException)
        {
            ErrorMessage = ex.Message;
            await LoadPlansAsync(cancellationToken);
            return Page();
        }
    }

    private async Task LoadPlansAsync(CancellationToken cancellationToken)
    {
        try
        {
            Plans = await _subscriptionService.ListPlansAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException)
        {
            Plans = Array.Empty<BillingPlan>();
            ErrorMessage ??= "Subscription plans are unavailable right now. Please try again shortly.";
        }
    }
}
