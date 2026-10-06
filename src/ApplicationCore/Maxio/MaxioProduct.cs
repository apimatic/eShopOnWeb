using System;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

public class MaxioProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public long? TrialPriceInCents { get; set; }
    public bool Taxable { get; set; }
    public bool RequireCreditCard { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public string? ProductFamilyHandle { get; set; }
}
