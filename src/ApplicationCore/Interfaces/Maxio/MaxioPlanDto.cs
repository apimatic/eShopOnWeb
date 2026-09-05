namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Maxio;

/// <summary>
/// A subscribable plan as defined in Maxio (a Product within the configured Product Family).
/// </summary>
public class MaxioPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}
