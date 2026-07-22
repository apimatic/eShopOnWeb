using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// UC1 — the storefront Plans page. Lists the available recurring plans and lets the signed-in
/// customer subscribe (mirrors the Basket Index page's structure).
/// </summary>
[Authorize]
public class PlansModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;

    public PlansModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public IReadOnlyCollection<SubscriptionPlan> Plans { get; private set; } = Array.Empty<SubscriptionPlan>();

    [TempData]
    public string? StatusMessage { get; set; }

    public string? ErrorMessage { get; private set; }

    public async Task OnGet()
    {
        await LoadPlansAsync();
    }

    public async Task<IActionResult> OnPostSubscribeAsync(string handle)
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));

        try
        {
            var subscription = await _subscriptionService.SubscribeAsync(User.Identity!.Name!, handle);
            StatusMessage = $"You are subscribed to {subscription.ProductName} at ${subscription.ProductPrice}/{subscription.Interval}. State: {subscription.State}.";
            return RedirectToPage("/Subscriptions/Mine");
        }
        catch (Exception ex)
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
            Plans = await _subscriptionService.ListPlansAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Plans are unavailable right now: {ex.Message}";
            Plans = Array.Empty<SubscriptionPlan>();
        }
    }
}
