using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// UC1 — browse the available plans and subscribe to one.
/// </summary>
[Authorize]
public class PlansModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;

    public PlansModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public IReadOnlyCollection<BillingPlan> Plans { get; private set; } = Array.Empty<BillingPlan>();

    public Subscription? Confirmation { get; private set; }

    public string? ErrorMessage { get; private set; }

    public async Task OnGet(CancellationToken cancellationToken)
    {
        await LoadPlansAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPost(string planHandle, CancellationToken cancellationToken)
    {
        try
        {
            Confirmation = await _subscriptionService.SubscribeAsync(User.Identity!.Name!, planHandle, cancellationToken);

            return RedirectToPage("./Mine");
        }
        catch (BillingConfigurationException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (BillingProviderException)
        {
            ErrorMessage = "We could not complete your subscription because the billing service is unavailable. Please try again shortly.";
        }

        await LoadPlansAsync(cancellationToken);

        return Page();
    }

    private async Task LoadPlansAsync(CancellationToken cancellationToken)
    {
        try
        {
            Plans = await _subscriptionService.ListPlansAsync(cancellationToken);
        }
        catch (BillingProviderException)
        {
            // A friendly message, and no enrollment is attempted (UC1 failure scenarios).
            ErrorMessage ??= "Subscription plans are unavailable right now. Please try again shortly.";
        }
    }
}
