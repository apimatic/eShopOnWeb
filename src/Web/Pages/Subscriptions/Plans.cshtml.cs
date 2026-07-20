using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

[Authorize]
public class PlansModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly IAppLogger<PlansModel> _logger;

    public PlansModel(ISubscriptionService subscriptionService, IAppLogger<PlansModel> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    public IReadOnlyList<BillingPlan> Plans { get; set; } = Array.Empty<BillingPlan>();
    public string? ErrorMessage { get; set; }

    public async Task OnGet()
    {
        await LoadPlansAsync();
    }

    public async Task<IActionResult> OnPostSubscribeAsync(string productHandle)
    {
        Guard.Against.NullOrEmpty(User?.Identity?.Name, nameof(User.Identity.Name));
        var userName = User.Identity!.Name!;

        try
        {
            await _subscriptionService.SubscribeAsync(userName, userName, FirstNameFrom(userName), "eShopOnWeb Customer", productHandle);
            return RedirectToPage("/Subscriptions/Mine");
        }
        catch (BillingConfigurationException ex)
        {
            _logger.LogWarning("Subscribe configuration error for plan {0}: {1}", productHandle, ex.Message);
            ErrorMessage = "This plan is temporarily unavailable. Please try again later.";
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Subscribe failed for user {0} on plan {1}: {2}", userName, productHandle, ex.Message);
            ErrorMessage = "We couldn't complete your subscription: " + ex.Message;
        }

        await LoadPlansAsync();
        return Page();
    }

    private async Task LoadPlansAsync()
    {
        try
        {
            Plans = await _subscriptionService.ListPlansAsync();
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Failed to list plans: {0}", ex.Message);
            ErrorMessage ??= "Plans are temporarily unavailable. Please try again shortly.";
        }
    }

    // eShopOnWeb's Identity user has no first/last name fields; derive a display name from the
    // email-style username so Maxio's required CreateCustomer.FirstName has a real value.
    private static string FirstNameFrom(string userName)
    {
        var at = userName.IndexOf('@');
        return at > 0 ? userName[..at] : userName;
    }
}
