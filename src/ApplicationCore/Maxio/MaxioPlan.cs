namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>A subscribable plan (Maxio product) exposed to eShopOnWeb shoppers.</summary>
public class MaxioPlan
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}
