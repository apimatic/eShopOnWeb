using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// The plans catalog and the hero subscribe action (UC1). Browsing is open to anyone; subscribing
/// requires a signed-in shopper.
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

    public IReadOnlyCollection<BillingPlan> Plans { get; private set; } = Array.Empty<BillingPlan>();

    /// <summary>Set when the plans could not be listed, so the page can degrade rather than fail.</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>Set after a successful enrollment so the page can confirm it.</summary>
    public CustomerSubscription? Subscribed { get; private set; }

    public async Task OnGet(CancellationToken cancellationToken)
    {
        await LoadPlansAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostSubscribe(string planHandle, CancellationToken cancellationToken)
    {
        if (!User.Identity?.IsAuthenticated ?? true)
        {
            return RedirectToPage("/Account/Login", new { area = "Identity" });
        }

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            ErrorMessage = "Choose a plan to subscribe to.";
            await LoadPlansAsync(cancellationToken);
            return Page();
        }

        Guard.Against.Null(User.Identity?.Name, nameof(User.Identity.Name));

        try
        {
            Subscribed = await _subscriptionService.SubscribeAsync(User.Identity!.Name!, planHandle, cancellationToken);
            return RedirectToPage("./Mine");
        }
        catch (BillingConfigurationException ex)
        {
            _logger.LogWarning("Subscribe to '{0}' failed on configuration: {1}", planHandle, ex.Message);
            ErrorMessage = "This plan is not available right now. Please contact support.";
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Subscribe to '{0}' was rejected by the billing provider: {1}", planHandle, ex.ProviderMessage);
            ErrorMessage = "We could not complete your subscription right now. Please try again shortly.";
        }

        await LoadPlansAsync(cancellationToken);
        return Page();
    }

    /// <summary>
    /// Loads the plan catalog. A provider outage or bad credentials leaves the page usable with a
    /// friendly message instead of throwing, and no enrollment is attempted (UC1 failure scenario).
    /// </summary>
    private async Task LoadPlansAsync(CancellationToken cancellationToken)
    {
        try
        {
            Plans = await _subscriptionService.ListPlansAsync(cancellationToken);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Plans could not be listed: {0}", ex.ProviderMessage);
            ErrorMessage ??= "Subscription plans are temporarily unavailable. Please try again shortly.";
        }
        catch (BillingConfigurationException ex)
        {
            _logger.LogWarning("Plans could not be listed because of configuration: {0}", ex.Message);
            ErrorMessage ??= "Subscription plans are not configured. Please contact support.";
        }
    }
}
