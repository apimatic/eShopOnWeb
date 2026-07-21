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

/// <summary>
/// UC2/UC3/UC4 management surface for the signed-in customer's own subscriptions. Mirrors
/// <c>OrderController.MyOrders</c>'s "[Authorize], view/manage" shape.
/// </summary>
[Authorize]
public class MineModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly IAppLogger<MineModel> _logger;

    public MineModel(ISubscriptionService subscriptionService, IAppLogger<MineModel> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    public IReadOnlyList<Subscription> Subscriptions { get; set; } = Array.Empty<Subscription>();
    public IReadOnlyList<BillingPlan> Plans { get; set; } = Array.Empty<BillingPlan>();
    public string? Message { get; set; }
    public string? ErrorMessage { get; set; }
    public PlanChangePreview? Preview { get; set; }
    public int? PreviewSubscriptionId { get; set; }

    public async Task OnGet()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostRecordUsageAsync(int subscriptionId, decimal quantity)
    {
        try
        {
            var result = await _subscriptionService.RecordUsageAsync(subscriptionId, quantity, "Reported from the storefront");
            Message = result.PeriodToDateAvailable
                ? $"Recorded {quantity} unit(s) against subscription #{subscriptionId}. Period-to-date usage: {result.PeriodToDateUnits}."
                : $"Recorded {quantity} unit(s) against subscription #{subscriptionId}. Period-to-date usage is temporarily unavailable.";
        }
        catch (Exception ex) when (ex is BillingConfigurationException or BillingProviderException or InvalidSubscriptionStateException or ArgumentException)
        {
            ErrorMessage = ex.Message;
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostPauseAsync(int subscriptionId) => await RunTransitionAsync(subscriptionId, () => _subscriptionService.PauseAsync(subscriptionId));

    public async Task<IActionResult> OnPostResumeAsync(int subscriptionId) => await RunTransitionAsync(subscriptionId, () => _subscriptionService.ResumeAsync(subscriptionId));

    public async Task<IActionResult> OnPostReactivateAsync(int subscriptionId) => await RunTransitionAsync(subscriptionId, () => _subscriptionService.ReactivateAsync(subscriptionId));

    public async Task<IActionResult> OnPostCancelAsync(int subscriptionId, bool endOfPeriod) =>
        await RunTransitionAsync(subscriptionId, () => _subscriptionService.CancelAsync(subscriptionId, endOfPeriod, reason: "Customer requested cancellation"));

    public async Task<IActionResult> OnPostPreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, bool applyNow)
    {
        try
        {
            Preview = await _subscriptionService.PreviewPlanChangeAsync(subscriptionId, targetPlanHandle, applyNow);
            PreviewSubscriptionId = subscriptionId;
        }
        catch (Exception ex) when (ex is BillingConfigurationException or BillingProviderException or InvalidSubscriptionStateException)
        {
            ErrorMessage = ex.Message;
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCommitPlanChangeAsync(int subscriptionId, string targetPlanHandle, bool applyNow, decimal expectedProratedAmount)
    {
        try
        {
            var updated = await _subscriptionService.CommitPlanChangeAsync(subscriptionId, targetPlanHandle, applyNow, expectedProratedAmount);
            Message = $"Subscription #{updated.Id} is now on plan '{updated.PlanHandle}'.";
        }
        catch (Exception ex) when (ex is BillingConfigurationException or BillingProviderException or InvalidSubscriptionStateException or PlanChangePreviewStaleException)
        {
            ErrorMessage = ex.Message;
        }

        await LoadAsync();
        return Page();
    }

    private async Task<IActionResult> RunTransitionAsync(int subscriptionId, Func<Task<Subscription>> transition)
    {
        try
        {
            var updated = await transition();
            Message = $"Subscription #{subscriptionId} is now {updated.Status}.";
        }
        catch (Exception ex) when (ex is BillingProviderException or InvalidSubscriptionStateException)
        {
            ErrorMessage = ex.Message;
        }

        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        var userName = User.Identity!.Name!;

        try
        {
            Subscriptions = await _subscriptionService.GetSubscriptionsForUserAsync(userName);
            Plans = await _subscriptionService.ListPlansAsync();
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Failed to load subscriptions for {0}: {1}", userName, ex.Message);
            ErrorMessage ??= "Your subscriptions are temporarily unavailable — please try again later.";
        }
    }
}
