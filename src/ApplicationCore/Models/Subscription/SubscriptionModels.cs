using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscription;

/// <summary>
/// A plan available for enrollment in the configured Maxio catalog.
/// <see cref="PriceInCents"/> is the live price read from Maxio; it is null when the
/// plan's product cannot currently be resolved there.
/// </summary>
public class SubscriptionPlan
{
    public string Handle { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public long? PriceInCents { get; set; }
    public bool IsDefault { get; set; }
    public bool Available { get; set; }

    /// <summary>The Maxio product family the plan belongs to (catalog context).</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;
}

/// <summary>
/// The confirmed state of one of the user's subscriptions as it stands in Maxio.
/// </summary>
public class SubscriptionSummary
{
    public int SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>True when the plan handle was resolved from Maxio itself.</summary>
    public bool PlanResolved { get; set; }

    /// <summary>Recurring charge in cents for the current period.</summary>
    public long? PriceInCents { get; set; }

    /// <summary>Maxio subscription state (e.g. active, canceled, past_due).</summary>
    public string? State { get; set; }

    /// <summary>Next billing date as Maxio reports it (next assessment).</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
}

/// <summary>
/// The outcome of a subscribe call: the confirmed subscription plus whether this
/// call created it (true) or found an existing enrollment (false).
/// </summary>
public class SubscribeOutcome
{
    public SubscriptionSummary Subscription { get; set; } = new();
    public bool CreatedNew { get; set; }
}