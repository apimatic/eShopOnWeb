using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Web.ViewModels.Subscriptions;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

/// <summary>
/// UC1 — the shopper browses the recurring plans and enrols in one.
/// </summary>
[Authorize]
public class PlansModel : PageModel
{
    private readonly ISubscriptionService _subscriptionService;

    public PlansModel(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public PlansViewModel PlansModelView { get; set; } = new PlansViewModel();

    public string? ErrorMessage { get; set; }

    public async Task OnGet()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostSubscribe(string planHandle)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            ErrorMessage = "Choose a plan to subscribe to.";
            await LoadAsync();
            return Page();
        }

        var userName = GetUserName();

        try
        {
            var subscription = await _subscriptionService.SubscribeAsync(userName, planHandle);

            TempData["SubscriptionStatus"] =
                $"You are subscribed to {subscription.PlanName ?? planHandle}. Next billing date: " +
                $"{subscription.NextBillingDate?.ToLocalTime().ToString("d") ?? "not scheduled"}.";

            return RedirectToPage("./Mine");
        }
        catch (BillingProviderException ex)
        {
            // The enrolment did not happen; the customer keeps whatever they had before.
            ErrorMessage = ex.ProviderMessage;
            await LoadAsync();
            return Page();
        }
    }

    private async Task LoadAsync()
    {
        var userName = GetUserName();

        try
        {
            PlansModelView.Plans = (await _subscriptionService.GetPlansAsync()).ToList();

            var active = await _subscriptionService.GetActiveSubscriptionAsync(userName);
            PlansModelView.CurrentPlanHandle = active?.PlanHandle;
        }
        catch (BillingProviderException ex)
        {
            // No enrolment is attempted when the catalog cannot be read.
            ErrorMessage = $"Subscription plans are unavailable right now. {ex.ProviderMessage}";
            PlansModelView.Plans = new List<ApplicationCore.Entities.SubscriptionAggregate.SubscriptionPlan>();
        }
    }

    private string GetUserName()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        return User.Identity.Name!;
    }
}
