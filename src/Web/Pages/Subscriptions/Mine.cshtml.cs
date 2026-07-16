using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Web.ViewModels;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// UC2-UC4: view/manage the signed-in customer's own subscriptions. Mirrors <c>OrderController.MyOrders</c>.
/// </summary>
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

    public List<SubscriptionViewModel> Subscriptions { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public string? StatusMessage { get; set; }

    public async Task OnGet()
    {
        await LoadSubscriptionsAsync();
    }

    public async Task<IActionResult> OnPostRecordUsageAsync(int subscriptionId, int quantity)
    {
        var userId = RequireUserId();
        try
        {
            var result = await _subscriptionService.RecordUsageAsync(userId, subscriptionId, quantity, "Reported from storefront", isAdmin: false);
            StatusMessage = result.PeriodToDateQuantity is { } total
                ? $"Recorded {result.QuantityRecorded} unit(s) of usage. Period-to-date: {total}. This will appear on your next renewal invoice."
                : $"Recorded {result.QuantityRecorded} unit(s) of usage. This will appear on your next renewal invoice.";
        }
        catch (Exception ex) when (ex is BillingProviderException or InvalidSubscriptionStateException or ArgumentException)
        {
            _logger.LogWarning("Record usage failed for subscription {0}: {1}", subscriptionId, ex.Message);
            ErrorMessage = ex.Message;
        }

        await LoadSubscriptionsAsync();
        return Page();
    }

    public Task<IActionResult> OnPostPauseAsync(int subscriptionId) =>
        ExecuteLifecycleAsync(subscriptionId, (userId, ct) => _subscriptionService.PauseAsync(userId, subscriptionId, isAdmin: false, ct));

    public Task<IActionResult> OnPostResumeAsync(int subscriptionId) =>
        ExecuteLifecycleAsync(subscriptionId, (userId, ct) => _subscriptionService.ResumeAsync(userId, subscriptionId, isAdmin: false, ct));

    public Task<IActionResult> OnPostReactivateAsync(int subscriptionId) =>
        ExecuteLifecycleAsync(subscriptionId, (userId, ct) => _subscriptionService.ReactivateAsync(userId, subscriptionId, isAdmin: false, ct));

    public Task<IActionResult> OnPostCancelAsync(int subscriptionId, bool cancelAtEndOfPeriod) =>
        ExecuteLifecycleAsync(subscriptionId, (userId, ct) =>
            _subscriptionService.CancelAsync(userId, subscriptionId, cancelAtEndOfPeriod, "Requested from storefront", isAdmin: false, ct));

    public async Task<IActionResult> OnPostPreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, bool applyImmediately)
    {
        var userId = RequireUserId();
        await LoadSubscriptionsAsync();

        try
        {
            var preview = await _subscriptionService.PreviewPlanChangeAsync(userId, subscriptionId, targetProductHandle, applyImmediately, isAdmin: false);
            var row = Subscriptions.FirstOrDefault(s => s.Id == subscriptionId);
            if (row is not null)
            {
                row.PendingPlanChangePreview = new PlanChangePreviewViewModel
                {
                    TargetProductHandle = preview.TargetProductHandle,
                    ApplyImmediately = preview.ApplyImmediately,
                    ProratedAdjustmentInCents = preview.ProratedAdjustmentInCents,
                    ChargeInCents = preview.ChargeInCents,
                    PaymentDueInCents = preview.PaymentDueInCents,
                    CreditAppliedInCents = preview.CreditAppliedInCents
                };
            }
        }
        catch (Exception ex) when (ex is BillingProviderException or InvalidSubscriptionStateException)
        {
            _logger.LogWarning("Plan change preview failed for subscription {0}: {1}", subscriptionId, ex.Message);
            ErrorMessage = ex.Message;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostCommitPlanChangeAsync(
        int subscriptionId, string targetProductHandle, bool applyImmediately,
        long proratedAdjustmentInCents, long chargeInCents, long paymentDueInCents, long creditAppliedInCents)
    {
        var userId = RequireUserId();
        var expectedPreview = new PlanChangePreview(targetProductHandle, applyImmediately, proratedAdjustmentInCents, chargeInCents, paymentDueInCents, creditAppliedInCents);

        try
        {
            await _subscriptionService.CommitPlanChangeAsync(userId, subscriptionId, targetProductHandle, applyImmediately, expectedPreview, isAdmin: false);
            StatusMessage = "Your plan change has been applied.";
        }
        catch (StalePlanChangePreviewException)
        {
            ErrorMessage = "The previewed amount has changed - please preview the plan change again before confirming.";
        }
        catch (Exception ex) when (ex is BillingProviderException or InvalidSubscriptionStateException)
        {
            _logger.LogWarning("Plan change commit failed for subscription {0}: {1}", subscriptionId, ex.Message);
            ErrorMessage = ex.Message;
        }

        await LoadSubscriptionsAsync();
        return Page();
    }

    private string RequireUserId()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        return User.Identity.Name!;
    }

    private async Task<IActionResult> ExecuteLifecycleAsync(int subscriptionId, Func<string, CancellationToken, Task> action)
    {
        var userId = RequireUserId();
        try
        {
            await action(userId, HttpContext.RequestAborted);
        }
        catch (Exception ex) when (ex is BillingProviderException or InvalidSubscriptionStateException)
        {
            _logger.LogWarning("Lifecycle action failed for subscription {0}: {1}", subscriptionId, ex.Message);
            ErrorMessage = ex.Message;
        }

        await LoadSubscriptionsAsync();
        return Page();
    }

    private async Task LoadSubscriptionsAsync()
    {
        var userId = RequireUserId();
        try
        {
            var subscriptions = await _subscriptionService.GetSubscriptionsForUserAsync(userId);
            Subscriptions = subscriptions.Select(MapRow).ToList();
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Unable to load subscriptions for {0}: {1}", userId, ex.Message);
            ErrorMessage ??= "We couldn't load your subscriptions right now. Please try again shortly.";
        }
    }

    private SubscriptionViewModel MapRow(Subscription subscription)
    {
        var alternateHandle = string.Equals(subscription.ProductHandle, _maxioSettings.DefaultProductHandle, StringComparison.OrdinalIgnoreCase)
            ? _maxioSettings.AlternateProductHandle
            : _maxioSettings.DefaultProductHandle;

        var isTerminal = subscription.State is SubscriptionState.Canceled or SubscriptionState.Expired or SubscriptionState.FailedToCreate;
        var isPaused = subscription.State is SubscriptionState.Paused or SubscriptionState.OnHold;

        return new SubscriptionViewModel
        {
            Id = subscription.Id,
            ProductHandle = subscription.ProductHandle,
            ProductName = subscription.ProductName,
            PriceInCents = subscription.PriceInCents,
            State = subscription.State.ToString(),
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
            CanPause = !isPaused && !isTerminal,
            CanResume = isPaused,
            CanCancel = !isTerminal,
            CanReactivate = isTerminal,
            CanChangePlan = !isTerminal,
            AlternatePlanHandle = alternateHandle
        };
    }
}
