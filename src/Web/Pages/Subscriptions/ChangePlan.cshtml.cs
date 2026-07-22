using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// Preview-then-confirm plan change (UC3). The customer always sees the prorated cost before
/// anything is committed, and the commit carries the signature of the preview they confirmed, so a
/// preview whose basis moved in the meantime is rejected rather than silently charging a different
/// amount.
/// </summary>
[Authorize]
public class ChangePlanModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly IAppLogger<ChangePlanModel> _logger;

    public ChangePlanModel(ISubscriptionService subscriptionService, IAppLogger<ChangePlanModel> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public int SubscriptionId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string TargetPlanHandle { get; set; } = string.Empty;

    /// <summary><c>Immediate</c> (prorated now) or <c>AtNextRenewal</c> (no proration).</summary>
    [BindProperty(SupportsGet = true)]
    public PlanChangeTiming Timing { get; set; } = PlanChangeTiming.Immediate;

    public PlanChangePreview? Preview { get; private set; }

    public string? ErrorMessage { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadPreviewAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostConfirmAsync(string previewSignature)
    {
        if (string.IsNullOrWhiteSpace(previewSignature))
        {
            await LoadPreviewAsync();
            ErrorMessage = "Review the cost and confirm again.";
            return Page();
        }

        try
        {
            var updated = await _subscriptionService.ChangePlanAsync(UserReference,
                SubscriptionId,
                TargetPlanHandle,
                Timing,
                previewSignature);

            StatusMessage = Timing == PlanChangeTiming.AtNextRenewal
                ? $"Your plan will change to {updated.PendingPlanHandle ?? TargetPlanHandle} at the next renewal."
                : $"Your plan is now {updated.PlanName ?? TargetPlanHandle}.";

            return RedirectToPage("./Mine");
        }
        catch (StalePlanChangePreviewException ex)
        {
            // The basis moved between display and confirmation — re-price and make them confirm again.
            _logger.LogWarning("Plan change on subscription {0} was refused as stale.", SubscriptionId);
            await LoadPreviewAsync();
            ErrorMessage = ex.Message;
        }
        catch (PlanChangeNotAllowedException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (SubscriptionNotFoundException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (BillingConfigurationException ex)
        {
            _logger.LogWarning("Plan change failed because the billing catalog is misconfigured: {0}", ex.Message);
            ErrorMessage = "That plan is not available right now. Please contact support.";
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Plan change on subscription {0} failed: {1}", SubscriptionId, ex.ProviderMessage);
            ErrorMessage = $"The billing provider refused that change: {ex.ProviderMessage}";
        }

        return Page();
    }

    private string UserReference
    {
        get
        {
            Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
            return User.Identity.Name;
        }
    }

    private async Task LoadPreviewAsync()
    {
        if (string.IsNullOrWhiteSpace(TargetPlanHandle))
        {
            ErrorMessage = "Choose a plan to move to.";
            return;
        }

        try
        {
            Preview = await _subscriptionService.PreviewPlanChangeAsync(UserReference,
                SubscriptionId,
                TargetPlanHandle,
                Timing);
        }
        catch (PlanChangeNotAllowedException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (SubscriptionNotFoundException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (BillingConfigurationException ex)
        {
            _logger.LogWarning("Plan change preview failed because the billing catalog is misconfigured: {0}", ex.Message);
            ErrorMessage = "That plan is not available right now. Please contact support.";
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Plan change preview for subscription {0} failed: {1}", SubscriptionId, ex.ProviderMessage);
            ErrorMessage = "We could not price that change just now. Please try again shortly.";
        }
    }
}
