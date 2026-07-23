namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The outcome of reporting metered usage against a subscription.
/// </summary>
/// <remarks>
/// The read-back of the running period-to-date total is deliberately best-effort: when the usage
/// was recorded but the read-back failed, <see cref="PeriodToDateUnits"/> is null and the operation
/// still reports success, rather than failing a write that already succeeded.
/// </remarks>
public class UsageRecordResult
{
    public long UsageId { get; init; }

    public int SubscriptionId { get; init; }

    public int ComponentId { get; init; }

    public required string ComponentHandle { get; init; }

    /// <summary>The quantity of units recorded by this call.</summary>
    public decimal Quantity { get; init; }

    public string? Memo { get; init; }

    /// <summary>Running unit balance for the current billing period, or null when the read-back was unavailable.</summary>
    public int? PeriodToDateUnits { get; init; }

    /// <summary>Estimated period-to-date charge in decimal currency units, when both the balance and a unit price are known.</summary>
    public decimal? PeriodToDateCharge { get; init; }

    /// <summary>True when the running total could not be read back after a successful write.</summary>
    public bool PeriodToDateUnavailable => PeriodToDateUnits is null;
}
