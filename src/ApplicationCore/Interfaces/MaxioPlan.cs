namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class MaxioPlan
{
    public string Handle { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = default!;
}
