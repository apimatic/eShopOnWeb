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
/// UC2/UC3/UC4 management surface: view subscriptions, report usage, preview/commit a plan change, and
/// run lifecycle actions. Mirrors OrderController.MyOrders as the "view/manage" page for this feature.
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

    public IReadOnlyList<BillingSubscription> Subscriptions { get; set; } = new List<BillingSubscription>();
    public IReadOnlyList<BillingPlan> Plans { get; set; } = new List<BillingPlan>();
    public PlanChangePreview? Preview { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    private string Username
    {
        get
        {
            Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
            return User.Identity.Name!;
        }
    }

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostRecordUsageAsync(int subscriptionId, int quantity)
    {
        await RunActionAsync(async () =>
        {
            var usage = await _subscriptionService.RecordUsageAsync(Username, subscriptionId, quantity, memo: "Reported from My Subscriptions", isAdmin: false);
            StatusMessage = usage.PeriodToDateBalance is int balance
                ? $"Recorded {usage.Quantity} unit(s) of usage. Period-to-date total: {balance}."
                : $"Recorded {usage.Quantity} unit(s) of usage.";
        });
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPauseAsync(int subscriptionId)
    {
        await RunActionAsync(async () =>
        {
            await _subscriptionService.PauseAsync(Username, subscriptionId, isAdmin: false);
            StatusMessage = "Subscription paused.";
        });
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResumeAsync(int subscriptionId)
    {
        await RunActionAsync(async () =>
        {
            await _subscriptionService.ResumeAsync(Username, subscriptionId, isAdmin: false);
            StatusMessage = "Subscription resumed.";
        });
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCancelAsync(int subscriptionId, bool endOfPeriod, string? reason)
    {
        await RunActionAsync(async () =>
        {
            await _subscriptionService.CancelAsync(Username, subscriptionId, endOfPeriod, reason, isAdmin: false);
            StatusMessage = endOfPeriod ? "Your subscription will cancel at the end of the current period." : "Subscription canceled.";
        });
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostReactivateAsync(int subscriptionId)
    {
        await RunActionAsync(async () =>
        {
            await _subscriptionService.ReactivateAsync(Username, subscriptionId, isAdmin: false);
            StatusMessage = "Subscription reactivated.";
        });
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, bool applyNow)
    {
        await LoadAsync();

        try
        {
            Preview = await _subscriptionService.PreviewPlanChangeAsync(Username, subscriptionId, targetPlanHandle, applyNow, isAdmin: false);
        }
        catch (InvalidSubscriptionStateException ex)
        {
            StatusMessage = ex.Message;
        }
        catch (SubscriptionNotFoundException ex)
        {
            StatusMessage = ex.Message;
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Plan-change preview failed for {Username}: {Message}", Username, ex.Message);
            StatusMessage = "We couldn't preview that plan change. Please try again.";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostCommitPlanChangeAsync(string previewToken)
    {
        await RunActionAsync(async () =>
        {
            var updated = await _subscriptionService.CommitPlanChangeAsync(Username, previewToken, isAdmin: false);
            StatusMessage = $"Plan changed to {updated.ProductName}.";
        });
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Subscriptions = await _subscriptionService.GetMySubscriptionsAsync(Username);

        try
        {
            Plans = await _subscriptionService.ListPlansAsync();
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Failed to list plans: {Message}", ex.Message);
        }
    }

    private async Task RunActionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (InvalidSubscriptionStateException ex)
        {
            StatusMessage = ex.Message;
        }
        catch (SubscriptionNotFoundException ex)
        {
            StatusMessage = ex.Message;
        }
        catch (SubscriptionAccessDeniedException ex)
        {
            StatusMessage = ex.Message;
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Subscription action failed for {Username}: {Message}", Username, ex.Message);
            StatusMessage = "We couldn't complete that action. Please try again.";
        }
    }
}
