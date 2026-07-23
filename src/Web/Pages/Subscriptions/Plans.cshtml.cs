using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// UC1 — browse the available recurring plans and subscribe to one.
/// </summary>
/// <remarks>
/// Browsing is anonymous so shoppers can see pricing; subscribing requires a signed-in customer,
/// which is enforced on the handler rather than the whole page.
/// </remarks>
public class PlansModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly IAppLogger<PlansModel> _logger;

    public PlansModel(ISubscriptionService subscriptionService, IAppLogger<PlansModel> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    public IReadOnlyList<BillingPlan> Plans { get; private set; } = Array.Empty<BillingPlan>();

    /// <summary>The plan the signed-in customer is already on, when they have one.</summary>
    public string? CurrentPlanHandle { get; private set; }

    public string? ErrorMessage { get; private set; }

    public string? StatusMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    [Authorize]
    public async Task<IActionResult> OnPostSubscribeAsync(string planHandle, CancellationToken cancellationToken)
    {
        var userReference = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userReference))
        {
            return Challenge();
        }

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            await LoadAsync(cancellationToken);
            ErrorMessage = "Choose a plan to subscribe to.";
            return Page();
        }

        try
        {
            var subscriber = new SubscriberIdentity(userReference, userReference);
            await _subscriptionService.SubscribeAsync(subscriber, planHandle, cancellationToken);

            return RedirectToPage("./Mine", new { subscribed = planHandle });
        }
        catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException)
        {
            _logger.LogWarning("Subscribe to '{PlanHandle}' failed for {User}: {Message}", planHandle, userReference, ex.Message);
            await LoadAsync(cancellationToken);
            ErrorMessage = ex.Message;
            return Page();
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            Plans = await _subscriptionService.ListPlansAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException)
        {
            // A friendly message rather than a yellow screen; no enrollment is attempted.
            _logger.LogWarning("Plans could not be listed: {Message}", ex.Message);
            Plans = Array.Empty<BillingPlan>();
            ErrorMessage = "Subscription plans are temporarily unavailable. Please try again shortly.";
            return;
        }

        var userReference = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userReference))
        {
            return;
        }

        try
        {
            var active = await _subscriptionService.GetActiveSubscriptionAsync(userReference, cancellationToken);
            CurrentPlanHandle = active?.PlanHandle;

            if (active is not null)
            {
                StatusMessage = $"You are currently subscribed to {active.PlanName ?? active.PlanHandle}.";
            }
        }
        catch (Exception ex) when (ex is BillingProviderException or BillingConfigurationException)
        {
            // The plan list is still perfectly usable without the customer's current state.
            _logger.LogWarning("Current subscription could not be read for {User}: {Message}", userReference, ex.Message);
        }
    }
}
