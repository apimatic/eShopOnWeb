namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// A Maxio product (plan) belonging to the configured product family.
/// </summary>
public class MaxioPlan
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}
