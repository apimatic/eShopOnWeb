namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;

/// <summary>
/// The outcome of recording usage (UC2). <see cref="PeriodToDateBalance"/> is null when the usage
/// was recorded successfully but the follow-up read-back of the running total failed — per the
/// integration plan, that read-back failure must not fail the whole operation.
/// </summary>
public sealed record BillingUsageRecordResult
{
    public required int RecordedQuantity { get; init; }
    public int? PeriodToDateBalance { get; init; }
}
