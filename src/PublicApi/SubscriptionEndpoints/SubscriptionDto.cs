using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The caller's subscription as recorded in Maxio Advanced Billing.
/// </summary>
public sealed class SubscriptionDto
{
    public long SubscriptionId { get; set; }

    public string State { get; set; } = string.Empty;

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    /// <summary>Recurring price in cents.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price, e.g. "299.00".</summary>
    public string Price { get; set; } = string.Empty;

    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    /// <summary>True when the subscription already existed and was not newly created.</summary>
    public bool AlreadySubscribed { get; set; }
}
