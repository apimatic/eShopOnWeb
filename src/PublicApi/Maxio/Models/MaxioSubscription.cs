using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

/// <summary>
/// A Maxio Subscription. Mirrors the subset of Subscription.yaml used by the integration.
/// The Maxio OpenAPI contract is authoritative for the member names.
/// </summary>
public class MaxioSubscription
{
    public long Id { get; set; }

    public string? State { get; set; }

    public long? BalanceInCents { get; set; }

    public long? ProductPriceInCents { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public bool? CancelAtEndOfPeriod { get; set; }

    public MaxioCustomer? Customer { get; set; }

    public MaxioProduct? Product { get; set; }
}
