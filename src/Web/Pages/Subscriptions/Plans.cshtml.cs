using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Web.ViewModels.Subscriptions;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// UC1's hero flow: browse the available plans and subscribe to one. Customer-facing subscription
/// pages run under cookie auth and identify the shopper by <c>User.Identity.Name</c> (§2.4/§4.4).
/// </summary>
[Authorize]
public class PlansModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;

    public PlansModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public IReadOnlyCollection<PlanViewModel> Plans { get; private set; } = Array.Empty<PlanViewModel>();

    /// <summary>Set when the plans could not be listed, so the page can explain rather than break.</summary>
    public string? ErrorMessage { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadPlansAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(string planHandle, CancellationToken cancellationToken)
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            await LoadPlansAsync(cancellationToken);
            ErrorMessage = "Choose a plan to subscribe to.";
            return Page();
        }

        try
        {
            var subscription = await _subscriptionService.SubscribeAsync(User.Identity.Name, planHandle, cancellationToken);
            StatusMessage = $"You are subscribed to {subscription.Billing.ProductName}.";
            return RedirectToPage("./Mine");
        }
        catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException or InvalidSubscriptionOperationException)
        {
            // Never enroll against a guessed plan and never show a stack trace: report what the
            // provider said and leave the customer on the Plans page (UC1 failure scenarios).
            await LoadPlansAsync(cancellationToken);
            ErrorMessage = ex.Message;
            return Page();
        }
    }

    private async Task LoadPlansAsync(CancellationToken cancellationToken)
    {
        try
        {
            var plans = await _subscriptionService.ListPlansAsync(cancellationToken);

            Plans = plans
                .OrderBy(p => p.PriceInCents)
                .Select(p => new PlanViewModel
                {
                    Handle = p.Handle,
                    Name = p.Name,
                    Description = p.Description,
                    Price = p.Price,
                    BillingInterval = FormatInterval(p.Interval, p.IntervalUnit),
                    RequiresPaymentMethod = p.RequiresPaymentMethod
                })
                .ToList();
        }
        catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException)
        {
            Plans = Array.Empty<PlanViewModel>();
            ErrorMessage = $"Subscription plans are unavailable right now. {ex.Message}";
        }
    }

    private static string FormatInterval(int interval, string intervalUnit)
    {
        if (string.IsNullOrWhiteSpace(intervalUnit))
        {
            return string.Empty;
        }

        return interval <= 1 ? intervalUnit : $"{interval} {intervalUnit}s";
    }
}
