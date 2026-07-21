using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The result of recording a metered usage event with the billing provider.
/// </summary>
public class BillingUsage
{
    public BillingUsage(long id, double quantity, string? memo, DateTimeOffset? createdAt, int? periodToDateBalance)
    {
        Id = id;
        Quantity = quantity;
        Memo = memo;
        CreatedAt = createdAt;
        PeriodToDateBalance = periodToDateBalance;
    }

    public long Id { get; }
    public double Quantity { get; }
    public string? Memo { get; }
    public DateTimeOffset? CreatedAt { get; }

    /// <summary>
    /// The running period-to-date total, read back immediately after recording. Null when the
    /// read-back failed - usage recording itself still succeeded (UC2 failure scenario).
    /// </summary>
    public int? PeriodToDateBalance { get; }
}
