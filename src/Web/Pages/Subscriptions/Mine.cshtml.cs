using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

    public IReadOnlyCollection<Subscription> Subscriptions { get; private set; } = new List<Subscription>();

    public Subscription? Current { get; private set; }

    public UsageReport? Usage { get; private set; }

    /// <summary>The other plan the customer can move to, when one is available.</summary>
    public BillingPlan? AlternatePlan { get; private set; }

    /// <summary>A proration preview awaiting the customer's confirmation.</summary>
    public PlanChangePreview? PendingPreview { get; private set; }

    public string? ErrorMessage { get; private set; }

    [TempData]
    public string? SubscriptionMessage { get; set; }

    public async Task OnGet(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostPreviewPlanChange(string targetPlanHandle, PlanChangeTiming timing,
        CancellationToken cancellationToken)
    {
        var userReference = RequireUserReference();

        return await ExecuteAsync(async () =>
        {
            PendingPreview = await _subscriptionService.PreviewPlanChangeAsync(userReference, targetPlanHandle,
                timing, cancellationToken);
        }, cancellationToken);
    }

    public async Task<IActionResult> OnPostChangePlan(string targetPlanHandle, PlanChangeTiming timing,
        string previewFingerprint, CancellationToken cancellationToken)
    {
        var userReference = RequireUserReference();

        try
        {
            var updated = await _subscriptionService.ChangePlanAsync(userReference, targetPlanHandle, timing,
                previewFingerprint, cancellationToken);

            SubscriptionMessage = timing == PlanChangeTiming.Immediately
                ? $"Your plan is now {updated.Plan.Name}."
                : $"Your plan changes to {targetPlanHandle} at your next renewal.";

            return RedirectToPage();
        }
        catch (StalePlanChangePreviewException ex)
        {
            await LoadAsync(cancellationToken);
            ErrorMessage = ex.Message;
            return Page();
        }
        catch (Exception ex) when (ex is BillingConfigurationException or BillingProviderException
                                       or InvalidSubscriptionTransitionException or InvalidOperationException)
        {
            await LoadAsync(cancellationToken);
            ErrorMessage = ex.Message;
            return Page();
        }
    }

    public async Task<IActionResult> OnPostLifecycle(SubscriptionLifecycleAction action,
        CancellationTiming cancellationTiming, string? reason, CancellationToken cancellationToken)
    {
        var userReference = RequireUserReference();

        try
        {
            var updated = await _subscriptionService.ExecuteLifecycleActionAsync(userReference, action,
                cancellationTiming, reason, cancellationToken);

            SubscriptionMessage = action == SubscriptionLifecycleAction.Cancel &&
                                  cancellationTiming == CancellationTiming.EndOfPeriod
                ? $"Your subscription will end on {Describe(updated.CurrentPeriodEndsAt)}."
                : $"Your subscription is now {updated.State}.";

            return RedirectToPage();
        }
        catch (Exception ex) when (ex is BillingConfigurationException or BillingProviderException
                                       or InvalidSubscriptionTransitionException or SubscriptionNotFoundException)
        {
            await LoadAsync(cancellationToken);
            ErrorMessage = ex.Message;
            return Page();
        }
    }

    public async Task<IActionResult> OnPostRecordUsage(decimal quantity, CancellationToken cancellationToken)
    {
        var userReference = RequireUserReference();

        if (quantity <= 0)
        {
            await LoadAsync(cancellationToken);
            ErrorMessage = "Enter a quantity greater than zero.";
            return Page();
        }

        try
        {
            var report = await _subscriptionService.RecordUsageAsync(userReference, quantity,
                "Reported from the storefront", cancellationToken);

            SubscriptionMessage = report.PeriodToDateUnitsAvailable
                ? $"Recorded {report.Record.Quantity} unit(s). {report.PeriodToDateUnits} unit(s) so far this period " +
                  $"({report.PeriodToDateCharge:C}) will appear on your next renewal invoice."
                : $"Recorded {report.Record.Quantity} unit(s). It will appear on your next renewal invoice.";

            return RedirectToPage();
        }
        catch (Exception ex) when (ex is BillingConfigurationException or BillingProviderException
                                       or InvalidSubscriptionTransitionException or SubscriptionNotFoundException)
        {
            await LoadAsync(cancellationToken);
            ErrorMessage = ex.Message;
            return Page();
        }
    }

    private async Task<IActionResult> ExecuteAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);

        try
        {
            await action();
        }
        catch (Exception ex) when (ex is BillingConfigurationException or BillingProviderException
                                       or InvalidSubscriptionTransitionException or SubscriptionNotFoundException
                                       or InvalidOperationException)
        {
            ErrorMessage = ex.Message;
        }

        return Page();
    }

    private string RequireUserReference()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        return User.Identity.Name;
    }

    /// <summary>
    /// Reads the customer's billing state. A provider outage degrades to a message rather than an
    /// error page, so the rest of the storefront keeps working.
    /// </summary>
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var userReference = RequireUserReference();

        try
        {
            Subscriptions = await _subscriptionService.GetSubscriptionsAsync(userReference, cancellationToken);
            Current = await _subscriptionService.GetCurrentSubscriptionAsync(userReference, cancellationToken);

            if (Current is not null)
            {
                var plans = await _subscriptionService.GetAvailablePlansAsync(cancellationToken);
                AlternatePlan = plans.FirstOrDefault(plan =>
                    !string.Equals(plan.Handle, Current.Plan.Handle, StringComparison.OrdinalIgnoreCase));

                Usage = await _subscriptionService.GetUsageSummaryAsync(userReference, cancellationToken);
            }
        }
        catch (BillingConfigurationException ex)
        {
            ErrorMessage = $"Your subscription is unavailable: {ex.Message}";
        }
        catch (BillingProviderException ex)
        {
            ErrorMessage = $"Your subscription is temporarily unavailable: {ex.ProviderMessage}";
        }
    }

    public static string Describe(DateTimeOffset? moment) =>
        moment.HasValue ? moment.Value.ToLocalTime().ToString("d MMMM yyyy") : "an unknown date";
}
