using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;
using Microsoft.eShopWeb.Web.ViewModels;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>UC2/UC3/UC4: view and manage the signed-in customer's subscriptions. Mirrors OrderController.MyOrders.</summary>
[Authorize]
public class MineModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;

    public MineModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public List<SubscriptionViewModel> Subscriptions { get; private set; } = new();
    public List<SubscriptionPlanViewModel> AvailablePlans { get; private set; } = new();
    public string? ErrorMessage { get; private set; }
    public string? StatusMessage { get; private set; }

    [BindProperty]
    public int PreviewedSubscriptionId { get; set; }
    [BindProperty]
    public string? PreviewedTargetPlanHandle { get; set; }
    [BindProperty]
    public bool PreviewedApplyNow { get; set; }
    public decimal? PreviewedAmount { get; private set; }
    public string? PreviewedEffectiveDate { get; private set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostPreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, bool applyNow)
    {
        var userReference = GetUserReference();

        try
        {
            var preview = await _subscriptionService.PreviewPlanChangeAsync(userReference, subscriptionId, targetPlanHandle, applyNow);
            PreviewedSubscriptionId = subscriptionId;
            PreviewedTargetPlanHandle = targetPlanHandle;
            PreviewedApplyNow = applyNow;
            PreviewedAmount = preview.ProratedAdjustmentInCents.HasValue ? preview.ProratedAdjustmentInCents.Value / 100m : (decimal?)null;
            PreviewedEffectiveDate = preview.EffectiveDate.ToString("d");
        }
        catch (SubscriptionValidationException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (BillingProviderException)
        {
            ErrorMessage = "We couldn't preview that plan change right now. Please try again later.";
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCommitPlanChangeAsync(int subscriptionId, string targetPlanHandle, bool applyNow, int? expectedProratedAdjustmentInCents)
    {
        var userReference = GetUserReference();

        try
        {
            var updated = await _subscriptionService.CommitPlanChangeAsync(userReference, subscriptionId, targetPlanHandle, applyNow, expectedProratedAdjustmentInCents);
            StatusMessage = applyNow
                ? $"Subscription {updated.Id} moved to {updated.PlanName} effective immediately."
                : $"Subscription {updated.Id} will move to plan '{targetPlanHandle}' at the next renewal.";
        }
        catch (SubscriptionValidationException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (BillingProviderException)
        {
            ErrorMessage = "We couldn't commit that plan change right now. Please try again later.";
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostPauseAsync(int subscriptionId)
    {
        await RunLifecycleActionAsync(subscriptionId, (svc, userRef, id, ct) => svc.PauseAsync(userRef, id, isAdmin: false, ct), "paused");
        return Page();
    }

    public async Task<IActionResult> OnPostResumeAsync(int subscriptionId)
    {
        await RunLifecycleActionAsync(subscriptionId, (svc, userRef, id, ct) => svc.ResumeAsync(userRef, id, isAdmin: false, ct), "resumed");
        return Page();
    }

    public async Task<IActionResult> OnPostCancelAsync(int subscriptionId, bool endOfPeriod)
    {
        await RunLifecycleActionAsync(subscriptionId, (svc, userRef, id, ct) => svc.CancelAsync(userRef, id, endOfPeriod, isAdmin: false, ct),
            endOfPeriod ? "scheduled to cancel at the end of the current period" : "cancelled");
        return Page();
    }

    public async Task<IActionResult> OnPostReactivateAsync(int subscriptionId)
    {
        await RunLifecycleActionAsync(subscriptionId, (svc, userRef, id, ct) => svc.ReactivateAsync(userRef, id, isAdmin: false, ct), "reactivated");
        return Page();
    }

    private async Task RunLifecycleActionAsync(int subscriptionId, Func<ISubscriptionService, string, int, CancellationToken, Task<BillingSubscription>> action, string verbDescribingSuccess)
    {
        var userReference = GetUserReference();

        try
        {
            var updated = await action(_subscriptionService, userReference, subscriptionId, HttpContext.RequestAborted);
            StatusMessage = $"Subscription {updated.Id} was {verbDescribingSuccess} (current state: {updated.State}).";
        }
        catch (SubscriptionValidationException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (BillingProviderException)
        {
            ErrorMessage = "We couldn't complete that action right now. Please try again later.";
        }

        await LoadAsync();
    }

    private string GetUserReference()
    {
        Guard.Against.Null(User.Identity?.Name, nameof(User.Identity.Name));
        return User.Identity!.Name!;
    }

    private async Task LoadAsync()
    {
        try
        {
            var subscriptions = await _subscriptionService.GetMySubscriptionsAsync(GetUserReference());
            var viewModels = new List<SubscriptionViewModel>();
            foreach (var subscription in subscriptions)
            {
                int? usageBalance = null;
                try
                {
                    usageBalance = await _subscriptionService.GetUsageBalanceAsync(GetUserReference(), subscription.Id, isAdmin: false);
                }
                catch (BillingProviderException)
                {
                    // Usage balance is a nice-to-have on this view; don't fail the whole page for it.
                }

                viewModels.Add(new SubscriptionViewModel
                {
                    Id = subscription.Id,
                    PlanHandle = subscription.PlanHandle,
                    PlanName = subscription.PlanName,
                    Price = subscription.PriceInCents / 100m,
                    State = subscription.State.ToString(),
                    NextBillingDate = subscription.NextBillingDate,
                    CancelAtPeriodEnd = subscription.CancelAtPeriodEnd,
                    PendingPlanHandle = subscription.PendingPlanHandle,
                    UsageBalance = usageBalance
                });
            }

            Subscriptions = viewModels;

            var plans = await _subscriptionService.ListPlansAsync();
            AvailablePlans = plans.Select(p => new SubscriptionPlanViewModel
            {
                Handle = p.Handle,
                Name = p.Name,
                Price = p.PriceInCents / 100m,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit
            }).ToList();
        }
        catch (BillingProviderException)
        {
            ErrorMessage ??= "We couldn't load your subscriptions right now. Please try again later.";
        }
    }
}
