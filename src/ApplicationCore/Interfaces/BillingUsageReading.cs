namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The outcome of recording usage: whether it was recorded, and (best-effort) the period-to-date running
/// total for the metered component. <see cref="PeriodToDateAvailable"/> is false when usage was recorded
/// successfully but the read-back of the running total failed (plan.md UC2 failure scenarios) — the caller
/// should report success with the total marked unavailable rather than failing the whole operation.
/// </summary>
public record BillingUsageReading(bool Recorded, int? PeriodToDateUnits, bool PeriodToDateAvailable);
