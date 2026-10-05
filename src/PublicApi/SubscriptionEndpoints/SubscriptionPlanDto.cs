namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A purchasable subscription plan, sourced from a Maxio product in the
/// configured product family.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Maxio product id (informational; handles are the stable identifier).</summary>
    public int ProductId { get; set; }

    /// <summary>Maxio product handle - stable identifier used to subscribe.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in integer cents.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price in major units (e.g. 299.00).</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>Billing interval count (e.g. 1).</summary>
    public int BillingInterval { get; set; }

    /// <summary>Billing interval unit, per the Maxio spec (e.g. "month").</summary>
    public string? BillingIntervalUnit { get; set; }

    public string? ProductFamilyHandle { get; set; }
}