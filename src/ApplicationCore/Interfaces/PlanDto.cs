namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class PlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Handle { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal PriceInDollars { get; set; }
    public string IntervalUnit { get; set; } = "";
    public int Interval { get; set; }
}
