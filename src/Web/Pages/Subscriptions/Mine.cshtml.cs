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

    public Subscription? Subscription { get; set; }
    public string? ErrorMessage { get; set; }
    public string? StatusMessage { get; set; }
    public PlanChangePreview? Preview { get; set; }
    public string? PreviewTargetHandle { get; set; }
    public bool PreviewApplyNow { get; set; }

    public async Task OnGetAsync()
    {
        await LoadSubscriptionAsync();
    }

    public async Task<IActionResult> OnPostRecordUsageAsync(double quantity, string? memo)
    {
        var userId = RequireUserId();

        if (await LoadSubscriptionAsync())
        {
            try
            {
                var usage = await _subscriptionService.RecordUsageAsync(userId, isAdmin: false, Subscription!.Id, quantity, memo);
                StatusMessage = $"Recorded {usage.Quantity} unit(s). Current period-to-date balance: {(usage.UnitBalance?.ToString() ?? "unavailable")}.";
            }
            catch (Exception ex) when (ex is BillingProviderException or IllegalSubscriptionTransitionException)
            {
                ErrorMessage = ex.Message;
            }
            catch (ArgumentException ex)
            {
                ErrorMessage = ex.Message;
            }
        }

        await LoadSubscriptionAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostPreviewPlanChangeAsync(string targetProductHandle, bool applyNow)
    {
        var userId = RequireUserId();

        if (await LoadSubscriptionAsync())
        {
            try
            {
                Preview = await _subscriptionService.PreviewPlanChangeAsync(userId, isAdmin: false, Subscription!.Id, targetProductHandle, applyNow);
                PreviewTargetHandle = targetProductHandle;
                PreviewApplyNow = applyNow;
            }
            catch (Exception ex) when (ex is BillingProviderException or IllegalSubscriptionTransitionException)
            {
                ErrorMessage = ex.Message;
            }
        }

        return Page();
    }

    public async Task<IActionResult> OnPostCommitPlanChangeAsync(string targetProductHandle, bool applyNow)
    {
        var userId = RequireUserId();

        if (await LoadSubscriptionAsync())
        {
            try
            {
                await _subscriptionService.CommitPlanChangeAsync(userId, isAdmin: false, Subscription!.Id, targetProductHandle, applyNow);
                StatusMessage = "Plan change applied.";
            }
            catch (Exception ex) when (ex is BillingProviderException or IllegalSubscriptionTransitionException)
            {
                ErrorMessage = ex.Message;
            }
        }

        await LoadSubscriptionAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostPauseAsync() => await RunLifecycleActionAsync(
        (userId, subscriptionId) => _subscriptionService.PauseAsync(userId, false, subscriptionId));

    public async Task<IActionResult> OnPostResumeAsync() => await RunLifecycleActionAsync(
        (userId, subscriptionId) => _subscriptionService.ResumeAsync(userId, false, subscriptionId));

    public async Task<IActionResult> OnPostReactivateAsync() => await RunLifecycleActionAsync(
        (userId, subscriptionId) => _subscriptionService.ReactivateAsync(userId, false, subscriptionId));

    public async Task<IActionResult> OnPostCancelAsync(bool endOfPeriod, string? reason) => await RunLifecycleActionAsync(
        (userId, subscriptionId) => _subscriptionService.CancelAsync(userId, false, subscriptionId, endOfPeriod, reason));

    private async Task<IActionResult> RunLifecycleActionAsync(Func<string, int, Task<Subscription>> action)
    {
        var userId = RequireUserId();

        if (await LoadSubscriptionAsync())
        {
            try
            {
                await action(userId, Subscription!.Id);
                StatusMessage = "Subscription updated.";
            }
            catch (Exception ex) when (ex is BillingProviderException or IllegalSubscriptionTransitionException)
            {
                ErrorMessage = ex.Message;
            }
        }

        await LoadSubscriptionAsync();
        return Page();
    }

    private async Task<bool> LoadSubscriptionAsync()
    {
        var userId = RequireUserId();
        Subscription = await _subscriptionService.GetMySubscriptionAsync(userId);
        if (Subscription == null)
        {
            ErrorMessage ??= "You do not have an active subscription.";
            return false;
        }

        return true;
    }

    private string RequireUserId()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        return User.Identity!.Name!;
    }
}
