using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// UC2/UC3/UC4 management surface: view the caller's subscriptions, record usage, preview/commit a
/// plan change, and drive lifecycle transitions. Mirrors OrderController's "MyOrders" role but as a
/// Razor Page (mutating POST handlers alongside the read model, like Pages/Basket/Index).
/// </summary>
[Authorize]
public class MineModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;

    public MineModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public IReadOnlyList<SubscriptionDto> Subscriptions { get; private set; } = Array.Empty<SubscriptionDto>();
    public IReadOnlyList<SubscriptionPlanDto> AvailablePlans { get; private set; } = Array.Empty<SubscriptionPlanDto>();
    public string? ErrorMessage { get; private set; }
    public string? StatusMessage { get; private set; }
    public UsageResultDto? LastUsage { get; private set; }
    public PlanChangePreviewDto? PendingPreview { get; private set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostRecordUsageAsync(int subscriptionId, double quantity, string? memo)
    {
        try
        {
            LastUsage = await _subscriptionService.RecordUsageAsync(subscriptionId, UserReference, IsAdmin, quantity, memo);
            StatusMessage = LastUsage.PeriodToDateAvailable
                ? $"Recorded {LastUsage.QuantityRecorded} unit(s) on subscription {subscriptionId}. Period-to-date: {LastUsage.PeriodToDateUnits} unit(s)."
                : $"Recorded {LastUsage.QuantityRecorded} unit(s) on subscription {subscriptionId}. Period-to-date total is unavailable right now.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostPreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, bool applyAtRenewal)
    {
        try
        {
            PendingPreview = await _subscriptionService.PreviewPlanChangeAsync(subscriptionId, UserReference, IsAdmin, targetProductHandle, applyAtRenewal);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCommitPlanChangeAsync(int subscriptionId, Guid previewToken)
    {
        try
        {
            var subscription = await _subscriptionService.CommitPlanChangeAsync(subscriptionId, UserReference, IsAdmin, previewToken);
            StatusMessage = $"Subscription {subscriptionId} is now on plan '{subscription.ProductHandle}'.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostLifecycleAsync(int subscriptionId, SubscriptionLifecycleAction action, bool endOfPeriod, string? reason)
    {
        try
        {
            var subscription = await _subscriptionService.ChangeLifecycleStateAsync(subscriptionId, UserReference, IsAdmin, action, endOfPeriod, reason);
            StatusMessage = $"Subscription {subscriptionId} is now {subscription.State}.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        Subscriptions = await _subscriptionService.GetSubscriptionsForUserAsync(UserReference);

        try
        {
            AvailablePlans = await _subscriptionService.ListPlansAsync();
        }
        catch (Exception)
        {
            AvailablePlans = Array.Empty<SubscriptionPlanDto>();
        }
    }

    private string UserReference
    {
        get
        {
            Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
            return User.Identity!.Name!;
        }
    }

    private bool IsAdmin => User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}
