namespace Microsoft.eShopWeb.Web.ViewModels.Subscriptions;

/// <summary>A plan as shown on the storefront Plans page (UC1 step 1).</summary>
public class PlanViewModel
{
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public decimal Price { get; set; }

    /// <summary>The billing interval rendered for display, e.g. "month" or "3 months".</summary>
    public string BillingInterval { get; set; } = string.Empty;

    /// <summary>True when the provider would demand card capture — the demo plans do not.</summary>
    public bool RequiresPaymentMethod { get; set; }
}
