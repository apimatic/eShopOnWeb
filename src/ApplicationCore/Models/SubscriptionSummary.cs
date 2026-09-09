namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// A subscription owned by an eShopOnWeb user, as recorded by the billing system of record.
/// </summary>
public record SubscriptionSummary(
    int SubscriptionId,
    int CustomerId,
    string State,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    DateTimeOffset? CreatedAtUtc,
    DateTimeOffset? ActivatedAtUtc,
    DateTimeOffset? NextBillingDateUtc,
    DateTimeOffset? CancelledAtUtc,
    bool CancelAtEndOfPeriod);
