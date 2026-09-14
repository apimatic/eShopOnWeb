using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Dtos;

/// <summary>Billing API wraps every resource in a single-property envelope.</summary>
internal sealed class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioProduct
{
    public long Id { get; set; }

    public string? Name { get; set; }

    public string? Handle { get; set; }

    public string? Description { get; set; }

    public long PriceInCents { get; set; }

    public int Interval { get; set; }

    public string? IntervalUnit { get; set; }

    public bool RequireCreditCard { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    public MaxioProductFamily? ProductFamily { get; set; }
}

internal sealed class MaxioProductFamily
{
    public long Id { get; set; }

    public string? Name { get; set; }

    public string? Handle { get; set; }
}

internal sealed class MaxioSiteEnvelope
{
    public MaxioSite? Site { get; set; }
}

internal sealed class MaxioSite
{
    public long Id { get; set; }

    public string? Name { get; set; }

    public string? Subdomain { get; set; }

    public string? Currency { get; set; }
}
