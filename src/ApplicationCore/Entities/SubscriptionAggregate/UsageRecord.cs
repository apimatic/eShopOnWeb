namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The result of recording pay-as-you-go usage against a subscription's metered component (UC2).
/// <see cref="PeriodToDateBalance"/> is null when the read-back of the running total failed after an
/// otherwise-successful usage record — the usage still stands (plan §UC2 failure scenarios).
/// </summary>
public record UsageRecord(
    long UsageId,
    double Quantity,
    string? Memo,
    int? PeriodToDateBalance);
