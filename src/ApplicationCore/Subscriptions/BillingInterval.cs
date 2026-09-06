namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// How often a plan recurs, expressed the way the Maxio specification models it: a numeric
/// <paramref name="Length"/> paired with an <paramref name="Unit"/> of <c>day</c> or <c>month</c>
/// (see maxio-spec/components/schemas/Interval-Unit.yaml).
/// </summary>
public record BillingInterval(int Length, string Unit)
{
    public override string ToString() =>
        Length == 1 ? $"every {Unit}" : $"every {Length} {Unit}s";
}
