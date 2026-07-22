namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// The outcome of recording metered usage: the receipt, plus the running period-to-date total when the
/// provider could be read back. A failed read-back leaves <see cref="PeriodToDateTotal"/> null rather than
/// failing the whole operation — the usage itself already stands.
/// </summary>
public class UsageReport
{
    public UsageReport(UsageReceipt receipt, decimal? periodToDateTotal)
    {
        Receipt = receipt;
        PeriodToDateTotal = periodToDateTotal;
    }

    public UsageReceipt Receipt { get; }

    /// <summary>
    /// Accumulated units for the current billing period, or <c>null</c> when it could not be read.
    /// </summary>
    public decimal? PeriodToDateTotal { get; }

    /// <summary>
    /// True when <see cref="PeriodToDateTotal"/> reflects a successful read-back.
    /// </summary>
    public bool IsPeriodToDateTotalAvailable => PeriodToDateTotal.HasValue;
}
