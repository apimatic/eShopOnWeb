using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

[Authorize]
public class PlansModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;

    public PlansModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public IReadOnlyList<BillingPlan> Plans { get; set; } = Array.Empty<BillingPlan>();
    public string? ErrorMessage { get; set; }

    public async Task OnGet()
    {
        try
        {
            Plans = await _subscriptionService.ListPlansAsync();
        }
        catch (BillingProviderException)
        {
            ErrorMessage = "We could not load plans right now. Please try again shortly.";
        }
    }

    public async Task<IActionResult> OnPostSubscribe(string productHandle)
    {
        Guard.Against.NullOrEmpty(productHandle, nameof(productHandle));
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));

        var userReference = User.Identity!.Name!;
        var localPart = userReference.Split('@')[0];

        try
        {
            await _subscriptionService.SubscribeAsync(userReference, userReference, localPart, "eShopOnWeb", productHandle);
        }
        catch (BillingProviderException)
        {
            ErrorMessage = "We could not complete your subscription right now. Please try again shortly.";
            Plans = await _subscriptionService.ListPlansAsync();
            return Page();
        }

        return RedirectToPage("./Mine");
    }
}
