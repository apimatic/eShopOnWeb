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
    private readonly IAppLogger<MineModel> _logger;

    public MineModel(ISubscriptionService subscriptionService, IAppLogger<MineModel> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    public IReadOnlyList<Subscription> MySubscriptions { get; set; } = Array.Empty<Subscription>();
    public IReadOnlyList<BillingPlan> AvailablePlans { get; set; } = Array.Empty<BillingPlan>();

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    // Carries the previewed proration forward to the confirm step without any server-side
    // session state — the whole preview/confirm round trip is stateless (§8).
    [TempData]
    public int? PreviewSubscriptionId { get; set; }
    [TempData]
    public string? PreviewTargetProductHandle { get; set; }
    [TempData]
    public int? PreviewProratedAdjustmentInCents { get; set; }
    [TempData]
    public int? PreviewChargeInCents { get; set; }
    [TempData]
    public int? PreviewPaymentDueInCents { get; set; }
    [TempData]
    public int? PreviewCreditAppliedInCents { get; set; }

    public async Task OnGet()
    {
        await LoadSubscriptionsAsync();

        // TempData is read-once by default: rendering the preview banner would otherwise
        // delete it before the user gets to click Confirm. Keep it alive for one more request.
        if (PreviewSubscriptionId.HasValue)
        {
            TempData.Keep(nameof(PreviewSubscriptionId));
            TempData.Keep(nameof(PreviewTargetProductHandle));
            TempData.Keep(nameof(PreviewProratedAdjustmentInCents));
            TempData.Keep(nameof(PreviewChargeInCents));
            TempData.Keep(nameof(PreviewPaymentDueInCents));
            TempData.Keep(nameof(PreviewCreditAppliedInCents));
        }
    }

    public async Task<IActionResult> OnPostRecordUsageAsync(int subscriptionId, int quantity)
    {
        var userReference = GetUserReference();
        try
        {
            var result = await _subscriptionService.RecordUsageAsync(userReference, isAdmin: false, subscriptionId, quantity, memo: "Recorded from My Subscriptions page");
            StatusMessage = result.PeriodToDateTotal.HasValue
                ? $"Recorded {result.QuantityRecorded} unit(s) of usage. Period-to-date total: {result.PeriodToDateTotal}."
                : $"Recorded {result.QuantityRecorded} unit(s) of usage. (Running total is temporarily unavailable.)";
        }
        catch (Exception ex) when (IsExpectedSubscriptionException(ex))
        {
            _logger.LogWarning("Record usage failed for subscription {0}: {1}", subscriptionId, ex.Message);
            ErrorMessage = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPreviewPlanChangeAsync(int subscriptionId, string targetProductHandle)
    {
        var userReference = GetUserReference();
        try
        {
            var preview = await _subscriptionService.PreviewPlanChangeAsync(userReference, subscriptionId, targetProductHandle);
            PreviewSubscriptionId = subscriptionId;
            PreviewTargetProductHandle = targetProductHandle;
            PreviewProratedAdjustmentInCents = preview.ProratedAdjustmentInCents;
            PreviewChargeInCents = preview.ChargeInCents;
            PreviewPaymentDueInCents = preview.PaymentDueInCents;
            PreviewCreditAppliedInCents = preview.CreditAppliedInCents;
        }
        catch (Exception ex) when (IsExpectedSubscriptionException(ex))
        {
            _logger.LogWarning("Preview plan change failed for subscription {0}: {1}", subscriptionId, ex.Message);
            ErrorMessage = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostConfirmPlanChangeAsync()
    {
        var userReference = GetUserReference();

        if (PreviewSubscriptionId is null || string.IsNullOrEmpty(PreviewTargetProductHandle))
        {
            ErrorMessage = "There is no pending plan-change preview to confirm. Please preview a plan change first.";
            return RedirectToPage();
        }

        try
        {
            var preview = new BillingPlanChangePreview(
                PreviewProratedAdjustmentInCents ?? 0, PreviewChargeInCents ?? 0, PreviewPaymentDueInCents ?? 0, PreviewCreditAppliedInCents ?? 0);
            await _subscriptionService.ChangePlanNowAsync(userReference, PreviewSubscriptionId.Value, PreviewTargetProductHandle, preview);
            StatusMessage = "Plan change applied.";
        }
        catch (StalePlanPreviewException ex)
        {
            _logger.LogWarning("Stale plan preview for subscription {0}: {1}", PreviewSubscriptionId, ex.Message);
            ErrorMessage = ex.Message;
        }
        catch (Exception ex) when (IsExpectedSubscriptionException(ex))
        {
            _logger.LogWarning("Confirm plan change failed for subscription {0}: {1}", PreviewSubscriptionId, ex.Message);
            ErrorMessage = ex.Message;
        }

        // Whether it succeeded or was rejected as stale, this preview is spent — clear it so it
        // never lingers or gets silently reapplied (§ UC3 failure scenarios).
        PreviewSubscriptionId = null;
        PreviewTargetProductHandle = null;
        PreviewProratedAdjustmentInCents = null;
        PreviewChargeInCents = null;
        PreviewPaymentDueInCents = null;
        PreviewCreditAppliedInCents = null;

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostScheduleForRenewalAsync(int subscriptionId, string targetProductHandle)
    {
        var userReference = GetUserReference();
        try
        {
            await _subscriptionService.SchedulePlanChangeAsync(userReference, subscriptionId, targetProductHandle);
            StatusMessage = "Plan change scheduled for your next renewal.";
        }
        catch (Exception ex) when (IsExpectedSubscriptionException(ex))
        {
            _logger.LogWarning("Schedule plan change failed for subscription {0}: {1}", subscriptionId, ex.Message);
            ErrorMessage = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPauseAsync(int subscriptionId)
        => await RunLifecycleActionAsync(subscriptionId, (svc, user) => svc.PauseAsync(user, isAdmin: false, subscriptionId), "Subscription paused.");

    public async Task<IActionResult> OnPostResumeAsync(int subscriptionId)
        => await RunLifecycleActionAsync(subscriptionId, (svc, user) => svc.ResumeAsync(user, isAdmin: false, subscriptionId), "Subscription resumed.");

    public async Task<IActionResult> OnPostCancelNowAsync(int subscriptionId)
        => await RunLifecycleActionAsync(subscriptionId, (svc, user) => svc.CancelAsync(user, isAdmin: false, subscriptionId, endOfPeriod: false, reason: "Customer requested cancellation"), "Subscription canceled.");

    public async Task<IActionResult> OnPostCancelEndOfPeriodAsync(int subscriptionId)
        => await RunLifecycleActionAsync(subscriptionId, (svc, user) => svc.CancelAsync(user, isAdmin: false, subscriptionId, endOfPeriod: true, reason: "Customer requested cancellation"), "Subscription will cancel at the end of the current period.");

    public async Task<IActionResult> OnPostReactivateAsync(int subscriptionId)
        => await RunLifecycleActionAsync(subscriptionId, (svc, user) => svc.ReactivateAsync(user, isAdmin: false, subscriptionId), "Subscription reactivated.");

    private async Task<IActionResult> RunLifecycleActionAsync(int subscriptionId, Func<ISubscriptionService, string, Task<Subscription>> action, string successMessage)
    {
        var userReference = GetUserReference();
        try
        {
            await action(_subscriptionService, userReference);
            StatusMessage = successMessage;
        }
        catch (Exception ex) when (IsExpectedSubscriptionException(ex))
        {
            _logger.LogWarning("Lifecycle action failed for subscription {0}: {1}", subscriptionId, ex.Message);
            ErrorMessage = ex.Message;
        }

        return RedirectToPage();
    }

    private static bool IsExpectedSubscriptionException(Exception ex) =>
        ex is BillingProviderException or InvalidSubscriptionStateException or SubscriptionNotFoundException or ArgumentException;

    private async Task LoadSubscriptionsAsync()
    {
        var userReference = GetUserReference();
        MySubscriptions = await _subscriptionService.GetSubscriptionsForUserAsync(userReference);

        try
        {
            AvailablePlans = await _subscriptionService.ListPlansAsync();
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Could not list subscription plans for plan-change options: {0}", ex.Message);
        }
    }

    private string GetUserReference()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        return User.Identity!.Name!;
    }
}
