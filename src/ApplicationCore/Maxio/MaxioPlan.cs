namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// A subscribable plan (Maxio "product") within the configured product family.
/// </summary>
public class MaxioPlan
{
    public MaxioPlan(string handle, string name, int priceInCents, int interval, string intervalUnit, bool requiresPaymentMethod)
    {
        Handle = handle;
        Name = name;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        RequiresPaymentMethod = requiresPaymentMethod;
    }

    public string Handle { get; }
    public string Name { get; }
    public int PriceInCents { get; }
    public int Interval { get; }
    public string IntervalUnit { get; }
    public bool RequiresPaymentMethod { get; }
}
