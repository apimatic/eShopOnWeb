using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// The storefront plan catalogue and the hero "Subscribe" action (UC1).
/// </summary>
/// <remarks>
/// Anyone may browse the plans; subscribing requires a signed-in customer, so an anonymous visitor
/// who clicks Subscribe is sent to the login page and returned here afterwards.
/// </remarks>
public class PlansModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;

    public PlansModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public IReadOnlyList<SubscriptionPlan> Plans { get; private set; } = Array.Empty<SubscriptionPlan>();

    /// <summary>A friendly message shown in place of the catalogue when it cannot be listed.</summary>
    public string? ErrorMessage { get; private set; }

    public async Task OnGet(CancellationToken cancellationToken)
    {
        await LoadPlansAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostSubscribe(string planHandle, CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return RedirectToPage("/Account/Login", new { area = "Identity", returnUrl = Url.Page("./Plans") });
        }

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            await LoadPlansAsync(cancellationToken);
            ErrorMessage = "Choose a plan to subscribe to.";
            return Page();
        }

        try
        {
            var subscription = await _subscriptionService.SubscribeAsync(
                User.Identity!.Name!, planHandle, cancellationToken);

            return RedirectToPage("./Mine", new { highlight = subscription.Id });
        }
        catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException
                                       or InvalidSubscriptionOperationException)
        {
            await LoadPlansAsync(cancellationToken);
            ErrorMessage = ex.Message;
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
            // No enrollment is attempted when the catalogue cannot be read.
            Plans = Array.Empty<SubscriptionPlan>();
            ErrorMessage = $"Subscription plans are unavailable right now. {ex.Message}";
        }
    }
}
