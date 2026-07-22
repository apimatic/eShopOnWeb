using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// The storefront plans page (UC1, steps 1-2). Browsing is open to anyone; subscribing requires a
/// signed-in customer.
/// </summary>
public class PlansModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly IAppLogger<PlansModel> _logger;

    public PlansModel(ISubscriptionService subscriptionService, IAppLogger<PlansModel> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    public IReadOnlyList<SubscriptionPlan> Plans { get; private set; } = Array.Empty<SubscriptionPlan>();

    /// <summary>The plan the signed-in customer is currently subscribed to, if any.</summary>
    public string? CurrentPlanHandle { get; private set; }

    public int? CurrentSubscriptionId { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostSubscribeAsync(string planHandle)
    {
        if (User?.Identity?.IsAuthenticated != true)
        {
            return RedirectToPage("/Account/Login", new { area = "Identity", returnUrl = Url.Page("./Plans") });
        }

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            ErrorMessage = "Choose a plan to subscribe to.";
            return RedirectToPage();
        }

        try
        {
            var subscription = await _subscriptionService.SubscribeAsync(User.Identity!.Name!, planHandle);

            StatusMessage = subscription.NextBillingAt.HasValue
                ? $"You are subscribed to {subscription.PlanName ?? planHandle} at {subscription.PlanPrice:C}. " +
                  $"Your next billing date is {subscription.NextBillingAt.Value:d}."
                : $"You are subscribed to {subscription.PlanName ?? planHandle} at {subscription.PlanPrice:C}.";

            return RedirectToPage("./Mine");
        }
        catch (DuplicateSubscriptionException ex)
        {
            // Already subscribed to something else — changing plans is a different flow so the
            // customer sees the proration before anything is charged.
            ErrorMessage = ex.Message;
        }
        catch (BillingConfigurationException ex)
        {
            _logger.LogWarning("Subscribe failed because the billing catalog is misconfigured: {0}", ex.Message);
            ErrorMessage = "This plan is not available right now. Please contact support.";
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Subscribe to '{0}' failed: {1}", planHandle, ex.ProviderMessage);
            ErrorMessage = "We could not complete your subscription just now. Please try again shortly.";
        }

        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        try
        {
            Plans = await _subscriptionService.ListPlansAsync();
        }
        catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException)
        {
            // No enrolment is attempted when plans cannot be listed — the page degrades to a notice.
            _logger.LogWarning("Subscription plans could not be listed: {0}", ex.Message);
            ErrorMessage ??= "Subscription plans are unavailable right now. Please try again shortly.";
            return;
        }

        if (User?.Identity?.IsAuthenticated != true)
        {
            return;
        }

        try
        {
            var mine = await _subscriptionService.ListMySubscriptionsAsync(User.Identity!.Name!);
            var live = mine.FirstOrDefault(subscription => subscription.IsLive);
            CurrentPlanHandle = live?.PlanHandle;
            CurrentSubscriptionId = live?.Id;
        }
        catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException)
        {
            // Failing to read the customer's own state must not hide the plans catalogue.
            _logger.LogWarning("Existing subscriptions could not be read for the plans page: {0}", ex.Message);
        }
    }
}
