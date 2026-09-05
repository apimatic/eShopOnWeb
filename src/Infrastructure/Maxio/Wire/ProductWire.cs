using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Wire;

internal class ProductEnvelope
{
    public ProductWire? Product { get; set; }
}

internal class ProductWire
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}
