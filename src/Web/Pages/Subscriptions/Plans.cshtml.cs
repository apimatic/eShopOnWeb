using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>UC1 — browse plans and subscribe (mirrors Pages/Basket/Index's storefront pattern).</summary>
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
    public Subscription? ActiveSubscription { get; set; }
    public string? ErrorMessage { get; set; }

    public async Task OnGet()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostSubscribeAsync(string productHandle)
    {
        var userId = RequireUserId();

        try
        {
            await _subscriptionService.SubscribeAsync(userId, userId, productHandle);
            return RedirectToPage("/Subscriptions/Mine");
        }
        catch (Exception ex) when (IsBillingFailure(ex))
        {
            _logger.LogWarning("Subscribe failed for user {UserId}: {Message}", userId, ex.Message);
            ErrorMessage = "We couldn't complete your subscription right now. Please try again shortly.";
        }

        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        var userId = RequireUserId();

        try
        {
            Plans = await _subscriptionService.ListPlansAsync();
            var subscriptions = await _subscriptionService.GetSubscriptionsForUserAsync(userId);
            ActiveSubscription = subscriptions.FirstOrDefault(s => s.IsActiveOrTrialing);
        }
        catch (Exception ex) when (IsBillingFailure(ex))
        {
            _logger.LogWarning("Failed to load plans for user {UserId}: {Message}", userId, ex.Message);
            ErrorMessage ??= "Plans are temporarily unavailable. Please try again shortly.";
        }
    }

    private string RequireUserId()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        return User.Identity!.Name!;
    }

    private static bool IsBillingFailure(Exception ex) =>
        ex is BillingConfigurationException or BillingProviderException;
}
