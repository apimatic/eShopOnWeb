using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The outcome of recording pay-as-you-go usage (UC2).
/// </summary>
/// <remarks>
/// The recorded usage is the part that must not be lost. Reading the running period-to-date
/// total back is a convenience: if that read fails the usage still stands, so the total is
/// reported as unavailable with a reason rather than failing the whole operation.
/// </remarks>
public class UsageReport
{
    private UsageReport(UsageRecord recorded,
        int? periodToDateQuantity,
        decimal? periodToDateCharge,
        string? totalUnavailableReason)
    {
        Recorded = recorded;
        PeriodToDateQuantity = periodToDateQuantity;
        PeriodToDateCharge = periodToDateCharge;
        TotalUnavailableReason = totalUnavailableReason;
    }

    /// <summary>The usage that was accepted by the provider.</summary>
    public UsageRecord Recorded { get; }

    /// <summary>Total units recorded so far in the current billing period, when it could be read.</summary>
    public int? PeriodToDateQuantity { get; }

    /// <summary>
    /// Period-to-date charge in whole currency units (dollars): the period-to-date quantity
    /// multiplied by the component's unit price.
    /// </summary>
    public decimal? PeriodToDateCharge { get; }

    /// <summary>Why the running total could not be read, when <see cref="IsTotalAvailable"/> is false.</summary>
    public string? TotalUnavailableReason { get; }

    public bool IsTotalAvailable => PeriodToDateQuantity.HasValue;

    public static UsageReport WithTotal(UsageRecord recorded, int periodToDateQuantity, decimal unitPrice)
    {
        Guard.Against.Null(recorded, nameof(recorded));
        Guard.Against.Negative(periodToDateQuantity, nameof(periodToDateQuantity));
        Guard.Against.Negative(unitPrice, nameof(unitPrice));

        return new UsageReport(recorded, periodToDateQuantity, periodToDateQuantity * unitPrice, null);
    }

    public static UsageReport WithoutTotal(UsageRecord recorded, string reason)
    {
        Guard.Against.Null(recorded, nameof(recorded));
        Guard.Against.NullOrEmpty(reason, nameof(reason));

        return new UsageReport(recorded, null, null, reason);
    }
}
