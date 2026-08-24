using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

// Mirrors components/schemas/Product-Response.yaml
public class MaxioProductResponse
{
    public MaxioProduct? Product { get; set; }
}

// Mirrors components/schemas/Product.yaml (fields relevant to plan listing)
public class MaxioProduct
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public long? InitialChargeInCents { get; set; }
    public int? TrialInterval { get; set; }
    public string? TrialIntervalUnit { get; set; }
    public bool RequireCreditCard { get; set; }
    public string? ArchivedAt { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

// Mirrors components/schemas/Product-Family.yaml
public class MaxioProductFamily
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
}
