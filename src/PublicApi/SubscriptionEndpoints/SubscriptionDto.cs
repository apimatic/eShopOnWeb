using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription as recorded in the billing system of record.
/// </summary>
public class SubscriptionDto
{
    /// <summary>
    /// The subscription id in Maxio Advanced Billing.
    /// </summary>
    public int SubscriptionId { get; set; }
    /// <summary>
    /// The Maxio customer id the subscription belongs to.
    /// </summary>
    public int CustomerId { get; set; }
    /// <summary>
    /// The stable plan handle in Maxio Advanced Billing.
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    /// <summary>
    /// The recurring price formatted as a decimal string (e.g. "299.00").
    /// </summary>
    public string Price { get; set; } = string.Empty;
    /// <summary>
    /// Maxio subscription state (e.g. active, trialing, past_due, canceled).
    /// </summary>
    public string State { get; set; } = string.Empty;
    /// <summary>
    /// The next billing date, when known.
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }

    public static SubscriptionDto From(ApplicationCore.Entities.SubscriptionAggregate.SubscriptionDetails details)
    {
        return new SubscriptionDto
        {
            SubscriptionId = details.MaxioSubscriptionId,
            CustomerId = details.MaxioCustomerId,
            PlanHandle = details.PlanHandle,
            PlanName = details.PlanName,
            PriceInCents = details.PriceInCents,
            Price = FormatPrice(details.PriceInCents),
            State = details.State,
            NextBillingAt = details.NextBillingAt,
            CreatedAt = details.CreatedAt
        };
    }

    public static string FormatPrice(long priceInCents)
    {
        return (priceInCents / 100m).ToString("0.00");
    }
}
