using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
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

    public Subscription? Subscription { get; set; }
    public UsagePeriodSummary? UsageSummary { get; set; }
    public List<BillingPlan> AlternatePlans { get; set; } = new();
    public PlanChangePreview? Preview { get; set; }
    public string? ErrorMessage { get; set; }
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGet()
    {
        await LoadAsync();
        if (Subscription is null)
        {
            return RedirectToPage("Plans");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostRecordUsageAsync(double quantity, string? memo)
    {
        var buyerId = RequireBuyerId();

        try
        {
            await LoadAsync();
            Guard.Against.Null(Subscription, nameof(Subscription));

            var (_, summary) = await _subscriptionService.RecordUsageAsync(buyerId, isAdmin: false, Subscription!.Id, quantity, memo);
            UsageSummary = summary;
            StatusMessage = "Usage recorded.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostPreviewPlanChangeAsync(string targetProductHandle, bool immediate)
    {
        var buyerId = RequireBuyerId();

        try
        {
            await LoadAsync();
            Guard.Against.Null(Subscription, nameof(Subscription));

            Preview = await _subscriptionService.PreviewPlanChangeAsync(buyerId, isAdmin: false, Subscription!.Id, targetProductHandle, immediate);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostCommitPlanChangeAsync(string targetProductHandle, bool immediate, string commitToken)
    {
        var buyerId = RequireBuyerId();

        try
        {
            var subscription = await _subscriptionService.GetMySubscriptionAsync(buyerId);
            Guard.Against.Null(subscription, nameof(subscription));

            await _subscriptionService.CommitPlanChangeAsync(buyerId, isAdmin: false, subscription!.Id, targetProductHandle, immediate, commitToken);
            return RedirectToPage("Mine");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await LoadAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostPauseAsync()
        => await RunLifecycleActionAsync((buyerId, subscriptionId) => _subscriptionService.PauseAsync(buyerId, false, subscriptionId));

    public async Task<IActionResult> OnPostResumeAsync()
        => await RunLifecycleActionAsync((buyerId, subscriptionId) => _subscriptionService.ResumeAsync(buyerId, false, subscriptionId));

    public async Task<IActionResult> OnPostReactivateAsync()
        => await RunLifecycleActionAsync((buyerId, subscriptionId) => _subscriptionService.ReactivateAsync(buyerId, false, subscriptionId));

    public async Task<IActionResult> OnPostCancelAsync(bool endOfPeriod, string? reason)
        => await RunLifecycleActionAsync((buyerId, subscriptionId) => _subscriptionService.CancelAsync(buyerId, false, subscriptionId, endOfPeriod, reason));

    private async Task<IActionResult> RunLifecycleActionAsync(Func<string, int, Task<Subscription>> action)
    {
        var buyerId = RequireBuyerId();

        try
        {
            var subscription = await _subscriptionService.GetMySubscriptionAsync(buyerId);
            Guard.Against.Null(subscription, nameof(subscription));

            await action(buyerId, subscription!.Id);
            return RedirectToPage("Mine");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await LoadAsync();
            return Page();
        }
    }

    private async Task LoadAsync()
    {
        var buyerId = RequireBuyerId();

        Subscription = await _subscriptionService.GetMySubscriptionAsync(buyerId);
        if (Subscription is null)
        {
            return;
        }

        var plans = await _subscriptionService.ListPlansAsync();
        AlternatePlans = plans.Where(p => !string.Equals(p.Handle, Subscription.ProductHandle, StringComparison.OrdinalIgnoreCase)).ToList();

        if (Subscription.IsActive)
        {
            UsageSummary = await _subscriptionService.GetUsageSummaryAsync(buyerId, isAdmin: false, Subscription.Id);
        }
    }

    private string RequireBuyerId()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        return User.Identity.Name!;
    }
}
