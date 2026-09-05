using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// A subscription, projected from a Maxio Subscription for eShopOnWeb API consumers.
/// </summary>
public class SubscriptionDto
{
    public long MaxioSubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public string PriceFormatted => $"${PriceInCents / 100m:0.00}";

    /// <summary>
    /// One of the values enumerated in maxio-spec/components/schemas/Subscription-State.yaml.
    /// </summary>
    public string State { get; set; } = string.Empty;

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>
    /// When the next payment capture will be attempted; surfaced to the caller as the
    /// "next billing date".
    /// </summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
