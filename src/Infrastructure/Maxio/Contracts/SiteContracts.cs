namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

/// <summary>Maxio <c>Site Response</c> envelope.</summary>
public class SiteResponse
{
    public Site? Site { get; set; }
}

/// <summary>Maxio <c>Site</c> schema (subset consumed by this integration).</summary>
public class Site
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Subdomain { get; set; }

    /// <summary>The site's primary ISO 4217 currency.</summary>
    public string? Currency { get; set; }

    public bool RelationshipInvoicingEnabled { get; set; }

    /// <summary>A value of the specification's <c>Collection Method</c> enum.</summary>
    public string? DefaultPaymentCollectionMethod { get; set; }

    public bool Test { get; set; }
}
