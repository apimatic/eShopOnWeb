namespace Microsoft.eShopWeb.ApplicationCore.Integration.Maxio;

/// <summary>
/// A subscribable plan (Maxio product) within a product family.
/// </summary>
public class MaxioPlan
{
    public long Id { get; set; }
    public string? Handle { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
    public bool RequiresPaymentMethod { get; set; }
    public string? ProductFamilyHandle { get; set; }
}
