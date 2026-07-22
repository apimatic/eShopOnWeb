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

/// <summary>
/// UC1 (view) + UC2 (usage) + UC3 (plan change) + UC4 (lifecycle) — the customer's subscription
/// management surface (mirrors the Orders "My orders" view plus action forms).
/// </summary>
[Authorize]
public class MineModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;

    public MineModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public IReadOnlyCollection<CustomerSubscription> Subscriptions { get; private set; } = Array.Empty<CustomerSubscription>();

    public IReadOnlyCollection<SubscriptionPlan> Plans { get; private set; } = Array.Empty<SubscriptionPlan>();

    [TempData]
    public string? StatusMessage { get; set; }

    public string? ErrorMessage { get; private set; }

    // Populated when a plan-change preview has been requested, so the view can render a confirm form.
    public PlanChangePreview? Preview { get; private set; }
    public int PreviewSubscriptionId { get; private set; }

    public async Task OnGet()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostPauseAsync(int id)
        => await ActAsync(id, () => _subscriptionService.PauseAsync(id), "paused");

    public async Task<IActionResult> OnPostResumeAsync(int id)
        => await ActAsync(id, () => _subscriptionService.ResumeAsync(id), "resumed");

    public async Task<IActionResult> OnPostCancelAsync(int id, bool immediate)
        => await ActAsync(id, () => _subscriptionService.CancelAsync(id, immediate, "Canceled from storefront"),
            immediate ? "canceled immediately" : "scheduled to cancel at period end");

    public async Task<IActionResult> OnPostReactivateAsync(int id)
        => await ActAsync(id, () => _subscriptionService.ReactivateAsync(id), "reactivated");

    public async Task<IActionResult> OnPostRecordUsageAsync(int id, int quantity, string? memo)
    {
        await LoadAsync();
        if (!EnsureOwned(id))
        {
            return Page();
        }

        try
        {
            var usage = await _subscriptionService.RecordUsageAsync(id, quantity, memo);
            var totalText = usage.PeriodToDateTotal.HasValue ? usage.PeriodToDateTotal.Value.ToString("0.##") : "unavailable";
            StatusMessage = $"Recorded {usage.RecordedQuantity} unit(s) on subscription {id}. Period-to-date total: {totalText}. This will appear on the next renewal invoice.";
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return Page();
        }
    }

    public async Task<IActionResult> OnPostPreviewAsync(int id, string target, bool applyImmediately)
    {
        await LoadAsync();
        if (!EnsureOwned(id))
        {
            return Page();
        }

        try
        {
            Preview = await _subscriptionService.PreviewPlanChangeAsync(id, target, applyImmediately);
            PreviewSubscriptionId = id;
            return Page();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return Page();
        }
    }

    public async Task<IActionResult> OnPostChangePlanAsync(int id, string target, bool applyImmediately,
        decimal proratedAdjustment, decimal chargeAmount, decimal paymentDue, decimal creditApplied)
    {
        await LoadAsync();
        if (!EnsureOwned(id))
        {
            return Page();
        }

        try
        {
            var confirmed = new PlanChangePreview(target, applyImmediately, proratedAdjustment, chargeAmount, paymentDue, creditApplied);
            var updated = await _subscriptionService.ChangePlanAsync(id, target, applyImmediately, confirmed);
            StatusMessage = $"Subscription {id} moved to {updated.ProductName} ({updated.ProductHandle}). State: {updated.State}.";
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return Page();
        }
    }

    private async Task<IActionResult> ActAsync(int id, Func<Task<CustomerSubscription>> action, string verb)
    {
        await LoadAsync();
        if (!EnsureOwned(id))
        {
            return Page();
        }

        try
        {
            var updated = await action();
            StatusMessage = $"Subscription {id} {verb}. Current state: {updated.State}.";
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return Page();
        }
    }

    private bool EnsureOwned(int id)
    {
        if (Subscriptions.Any(s => s.Id == id))
        {
            return true;
        }

        ErrorMessage = $"Subscription {id} was not found on your account.";
        return false;
    }

    private async Task LoadAsync()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));

        try
        {
            Subscriptions = await _subscriptionService.GetSubscriptionsForUserAsync(User.Identity!.Name!);
            Plans = await _subscriptionService.ListPlansAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not load your subscriptions: {ex.Message}";
            Subscriptions = Array.Empty<CustomerSubscription>();
            Plans = Array.Empty<SubscriptionPlan>();
        }
    }
}
