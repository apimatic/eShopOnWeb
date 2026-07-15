namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;

/// <summary>
/// A recurring plan (Maxio product) a customer can subscribe to, as surfaced to the rest of
/// eShopOnWeb by <see cref="IBillingClient"/>.
/// </summary>
public sealed record BillingPlan
{
    public required string Handle { get; init; }
    public required string Name { get; init; }
    public required int PriceInCents { get; init; }
    public required int Interval { get; init; }
    public required string IntervalUnit { get; init; }
    public required bool RequiresPaymentMethod { get; init; }
}
