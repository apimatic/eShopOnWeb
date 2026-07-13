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

    public SubscriptionDetails? ActiveSubscription { get; set; }
    public ComponentUsageStatus? UsageStatus { get; set; }
    public IReadOnlyList<SubscriptionPlan> OtherPlans { get; set; } = Array.Empty<SubscriptionPlan>();
    public PlanChangePreview? Preview { get; set; }
    public string? ErrorMessage { get; set; }
    public string? StatusMessage { get; set; }

    public async Task OnGet()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostRecordUsage(int quantity, string? memo)
    {
        await LoadAsync();
        if (ActiveSubscription == null)
        {
            return Page();
        }

        try
        {
            UsageStatus = await _subscriptionService.RecordUsageAsync(ActiveSubscription.Id, quantity, memo);
            StatusMessage = UsageStatus.PeriodToDateUnavailable
                ? "Usage recorded. The running total is temporarily unavailable."
                : $"Usage recorded. Period-to-date: {UsageStatus.PeriodToDateUnitBalance} units.";
        }
        catch (BillingProviderException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (InvalidSubscriptionStateException ex)
        {
            ErrorMessage = ex.Message;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostPreviewPlanChange(string targetProductHandle, bool applyImmediately)
    {
        await LoadAsync();
        if (ActiveSubscription == null)
        {
            return Page();
        }

        try
        {
            Preview = await _subscriptionService.PreviewPlanChangeAsync(ActiveSubscription.Id, targetProductHandle, applyImmediately);
        }
        catch (BillingProviderException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (InvalidSubscriptionStateException ex)
        {
            ErrorMessage = ex.Message;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostCommitPlanChange(
        string currentProductHandle,
        string targetProductHandle,
        bool applyImmediately,
        decimal proratedAdjustmentInCents,
        decimal chargeInCents,
        decimal paymentDueInCents,
        decimal creditAppliedInCents)
    {
        await LoadAsync();
        if (ActiveSubscription == null)
        {
            return Page();
        }

        var confirmedPreview = new PlanChangePreview(
            currentProductHandle, targetProductHandle, applyImmediately,
            proratedAdjustmentInCents, chargeInCents, paymentDueInCents, creditAppliedInCents);

        try
        {
            ActiveSubscription = await _subscriptionService.CommitPlanChangeAsync(ActiveSubscription.Id, targetProductHandle, confirmedPreview);
            StatusMessage = $"Plan changed to {ActiveSubscription.ProductName}.";
        }
        catch (StalePlanChangePreviewException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (BillingProviderException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (InvalidSubscriptionStateException ex)
        {
            ErrorMessage = ex.Message;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostPause() => await PerformLifecycleAction(s => _subscriptionService.PauseAsync(s.Id));

    public async Task<IActionResult> OnPostResume() => await PerformLifecycleAction(s => _subscriptionService.ResumeAsync(s.Id));

    public async Task<IActionResult> OnPostCancel(bool endOfPeriod, string? reason) =>
        await PerformLifecycleAction(s => _subscriptionService.CancelAsync(s.Id, endOfPeriod, reason));

    public async Task<IActionResult> OnPostReactivate() => await PerformLifecycleAction(s => _subscriptionService.ReactivateAsync(s.Id));

    private async Task<IActionResult> PerformLifecycleAction(Func<SubscriptionDetails, Task<SubscriptionDetails>> action)
    {
        await LoadAsync();
        if (ActiveSubscription == null)
        {
            return Page();
        }

        try
        {
            ActiveSubscription = await action(ActiveSubscription);
            StatusMessage = $"Subscription is now {ActiveSubscription.State}.";
        }
        catch (InvalidSubscriptionStateException ex)
        {
            // Illegal transition from the current state: reject with the current state (UC4 failure scenario).
            ErrorMessage = ex.Message;
        }
        catch (BillingProviderException ex)
        {
            ErrorMessage = ex.Message;
        }

        return Page();
    }

    private async Task LoadAsync()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        var customerReference = User.Identity.Name!;

        try
        {
            ActiveSubscription = await _subscriptionService.GetCurrentSubscriptionAsync(customerReference);
            if (ActiveSubscription != null)
            {
                UsageStatus = await _subscriptionService.GetUsageStatusAsync(ActiveSubscription.Id);
                var allPlans = await _subscriptionService.ListPlansAsync();
                OtherPlans = allPlans.Where(p => p.Handle != ActiveSubscription.ProductHandle).ToList();
            }
        }
        catch (BillingProviderException ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
