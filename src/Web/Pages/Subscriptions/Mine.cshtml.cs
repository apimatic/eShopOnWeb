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
public class MineModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;

    public MineModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public IReadOnlyList<Subscription> Subscriptions { get; set; } = Array.Empty<Subscription>();
    public string? ErrorMessage { get; set; }
    public string? StatusMessage { get; set; }

    public async Task OnGet()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostUsage(int subscriptionId, int quantity)
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));

        try
        {
            var result = await _subscriptionService.RecordUsageAsync(subscriptionId, quantity, memo: null);
            StatusMessage = result.PeriodToDateUnits.HasValue
                ? $"Recorded {result.Quantity} unit(s). Period-to-date total: {result.PeriodToDateUnits}."
                : $"Recorded {result.Quantity} unit(s). Period-to-date total is temporarily unavailable.";
        }
        catch (BillingProviderException)
        {
            ErrorMessage = "We could not record usage right now. Please try again shortly.";
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostLifecycle(int subscriptionId, string action, bool endOfPeriod)
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        var userReference = User.Identity!.Name!;

        try
        {
            switch (action.ToLowerInvariant())
            {
                case "pause":
                    await _subscriptionService.PauseAsync(userReference, subscriptionId);
                    break;
                case "resume":
                    await _subscriptionService.ResumeAsync(userReference, subscriptionId);
                    break;
                case "cancel":
                    await _subscriptionService.CancelAsync(userReference, subscriptionId, endOfPeriod);
                    break;
                case "reactivate":
                    await _subscriptionService.ReactivateAsync(userReference, subscriptionId);
                    break;
                default:
                    ErrorMessage = $"Unknown action '{action}'.";
                    break;
            }
        }
        catch (BillingProviderException ex)
        {
            ErrorMessage = ex.Message;
        }

        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));

        try
        {
            Subscriptions = await _subscriptionService.GetMySubscriptionsAsync(User.Identity!.Name!);
        }
        catch (BillingProviderException)
        {
            ErrorMessage = "We could not load your subscriptions right now. Please try again shortly.";
        }
    }
}
