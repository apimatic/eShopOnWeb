using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

public class PlansModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;

    public PlansModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public IReadOnlyCollection<SubscriptionPlan> Plans { get; set; } = new List<SubscriptionPlan>();

    public string? ErrorMessage { get; set; }

    public async Task OnGet(CancellationToken cancellationToken)
    {
        await LoadPlansAsync(cancellationToken);
    }

    [Authorize]
    public async Task<IActionResult> OnPostSubscribe(string planHandle, CancellationToken cancellationToken)
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));

        try
        {
            await _subscriptionService.SubscribeAsync(User.Identity.Name, planHandle, cancellationToken);
        }
        catch (Exception exception) when (exception is BillingProviderException or BillingConfigurationException)
        {
            ErrorMessage = exception.Message;
            await LoadPlansAsync(cancellationToken);
            return Page();
        }

        return RedirectToPage("./Mine");
    }

    private async Task LoadPlansAsync(CancellationToken cancellationToken)
    {
        try
        {
            Plans = await _subscriptionService.GetAvailablePlansAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is BillingProviderException or BillingConfigurationException)
        {
            // No enrollment is attempted when plans cannot be listed.
            ErrorMessage = "Subscription plans are unavailable right now. Please try again later.";
            Plans = new List<SubscriptionPlan>();
        }
    }
}
