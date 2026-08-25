using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class SubscriptionPlanDto
{
    public int? Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
}

public class SubscriptionDto
{
    public int? Id { get; set; }
    public string? State { get; set; }
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public long? PriceInCents { get; set; }
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>
    /// End of the current paid period; the subscription read model has no next_billing_at,
    /// so this is the next-billing signal surfaced to the shopper.
    /// </summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
}

/// <summary>
/// Identity of the eShopOnWeb shopper, resolved from the JWT/identity store.
/// </summary>
public sealed record ShopperIdentity(string UserId, string Email, string FirstName, string LastName);
