using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// Browse the available subscription plans and enroll in one. Browsing is open; subscribing requires a
/// signed-in customer.
/// </summary>
public class PlansModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;

    public PlansModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public IReadOnlyList<BillingPlan> Plans { get; private set; } = Array.Empty<BillingPlan>();

    public string? ErrorMessage { get; private set; }

    public async Task OnGet()
    {
        await LoadPlansAsync();
    }

    public async Task<IActionResult> OnPostSubscribe(string planHandle)
    {
        if (User.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(User.Identity.Name))
        {
            return RedirectToPage("/Account/Login", new { area = "Identity" });
        }

        try
        {
            await _subscriptionService.SubscribeAsync(User.Identity.Name, planHandle);
            return RedirectToPage("./Mine");
        }
        catch (Exception ex) when (IsBillingFailure(ex))
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
            Plans = await _subscriptionService.GetAvailablePlansAsync();
        }
        catch (Exception ex) when (IsBillingFailure(ex))
        {
            // A provider outage shows a friendly message rather than an error page; no enrollment is
            // attempted.
            ErrorMessage ??= "Subscription plans are temporarily unavailable. Please try again shortly.";
            Plans = Array.Empty<BillingPlan>();
        }
    }

    private static bool IsBillingFailure(Exception exception) =>
        exception is BillingProviderException
            or BillingConfigurationException
            or InvalidSubscriptionOperationException;
}
