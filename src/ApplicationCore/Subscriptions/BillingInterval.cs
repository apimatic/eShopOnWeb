namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// How often a recurring charge is assessed, e.g. "every 1 month".
/// </summary>
public readonly record struct BillingInterval(int Length, BillingIntervalUnit Unit)
{
    public static readonly BillingInterval Unknown = new(0, BillingIntervalUnit.Unknown);

    public override string ToString() => Unit switch
    {
        BillingIntervalUnit.Unknown => "unknown",
        _ when Length == 1 => $"per {Unit.ToString().ToLowerInvariant()}",
        _ => $"every {Length} {Unit.ToString().ToLowerInvariant()}s"
    };
}

public enum BillingIntervalUnit
{
    Unknown = 0,
    Day = 1,
    Month = 2
}
