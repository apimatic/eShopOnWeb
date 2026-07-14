using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

[Authorize]
public class MineModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly MaxioSettings _maxioSettings;
    private readonly IAppLogger<MineModel> _logger;

    public MineModel(ISubscriptionService subscriptionService, IOptions<MaxioSettings> maxioSettings, IAppLogger<MineModel> logger)
    {
        _subscriptionService = subscriptionService;
        _maxioSettings = maxioSettings.Value;
        _logger = logger;
    }

    public Subscription? Subscription { get; private set; }
    public BillingPlan? AlternatePlan { get; private set; }
    public BillingProrationPreview? Preview { get; private set; }
    public bool PreviewApplyNow { get; private set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGet()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostPreviewAsync(string targetProductHandle, bool applyNow)
    {
        await LoadAsync();
        if (Subscription is null)
        {
            return RedirectToPage();
        }

        try
        {
            Preview = await _subscriptionService.PreviewPlanChangeAsync(Subscription.Id, targetProductHandle, applyNow);
            PreviewApplyNow = applyNow;
        }
        catch (Exception ex) when (ex is BillingProviderException or InvalidSubscriptionStateException)
        {
            ErrorMessage = ex.Message;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostConfirmPlanChangeAsync(
        string targetProductHandle,
        bool applyNow,
        int expectedProratedAdjustmentInCents,
        int expectedChargeInCents,
        int expectedPaymentDueInCents,
        int expectedCreditAppliedInCents)
    {
        await LoadAsync();
        if (Subscription is null)
        {
            return RedirectToPage();
        }

        try
        {
            var expectedPreview = new BillingProrationPreview(
                targetProductHandle, applyNow, expectedProratedAdjustmentInCents, expectedChargeInCents,
                expectedPaymentDueInCents, expectedCreditAppliedInCents);
            await _subscriptionService.ChangePlanAsync(Subscription.Id, targetProductHandle, applyNow, expectedPreview);
            StatusMessage = "Your plan change has been applied.";
        }
        catch (Exception ex) when (ex is BillingProviderException or InvalidSubscriptionStateException or StalePreviewException)
        {
            ErrorMessage = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRecordUsageAsync(int quantity, string? memo)
    {
        await LoadAsync();
        if (Subscription is null)
        {
            return RedirectToPage();
        }

        try
        {
            var balance = await _subscriptionService.RecordUsageAsync(Subscription.Id, quantity, memo);
            StatusMessage = balance.PeriodToDateUnitBalance is int total
                ? $"Recorded {balance.RecordedQuantity} unit(s). Period-to-date total: {total}."
                : $"Recorded {balance.RecordedQuantity} unit(s). Period-to-date total is unavailable right now.";
        }
        catch (Exception ex) when (ex is BillingProviderException or InvalidSubscriptionStateException)
        {
            ErrorMessage = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostLifecycleAsync(string action, bool endOfPeriod, string? reason)
    {
        await LoadAsync();
        if (Subscription is null)
        {
            return RedirectToPage();
        }

        try
        {
            await (action switch
            {
                "pause" => _subscriptionService.PauseAsync(Subscription.Id),
                "resume" => _subscriptionService.ResumeAsync(Subscription.Id),
                "cancel" => _subscriptionService.CancelAsync(Subscription.Id, endOfPeriod, reason),
                "reactivate" => _subscriptionService.ReactivateAsync(Subscription.Id),
                _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown lifecycle action."),
            });
            StatusMessage = "Subscription updated.";
        }
        catch (Exception ex) when (ex is BillingProviderException or InvalidSubscriptionStateException)
        {
            ErrorMessage = ex.Message;
        }

        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Guard.Against.NullOrEmpty(User?.Identity?.Name, nameof(User.Identity.Name));

        try
        {
            Subscription = await _subscriptionService.FindSubscriptionForUserAsync(User.Identity.Name);
            if (Subscription is not null)
            {
                var alternateHandle = string.Equals(Subscription.ProductHandle, _maxioSettings.DefaultProductHandle, StringComparison.OrdinalIgnoreCase)
                    ? _maxioSettings.AlternateProductHandle
                    : _maxioSettings.DefaultProductHandle;

                var plans = await _subscriptionService.ListPlansAsync();
                AlternatePlan = plans.FirstOrDefault(p => string.Equals(p.Handle, alternateHandle, StringComparison.OrdinalIgnoreCase));
            }
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Failed to load subscription for {UserName}: {Message}", User.Identity.Name, ex.Message);
            ErrorMessage = "We couldn't load your subscription right now. Please try again shortly.";
        }
    }
}
