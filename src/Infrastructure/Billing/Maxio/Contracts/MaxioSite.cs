namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

/// <summary>The specification's <c>Site-Response</c> schema.</summary>
public class SiteResponse
{
    public MaxioSite? Site { get; set; }
}

/// <summary>
/// The specification's <c>Site</c> schema, limited to the fields this integration consumes. The
/// site currency is used to present plan prices.
/// </summary>
public class MaxioSite
{
    public long Id { get; set; }

    public string? Name { get; set; }

    public string? Subdomain { get; set; }

    public string? Currency { get; set; }

    /// <summary>
    /// True on sites using the Relationship Invoicing architecture, which decides which values of
    /// the specification's <c>Collection-Method</c> enumeration are accepted.
    /// </summary>
    public bool RelationshipInvoicingEnabled { get; set; }

    public string? DefaultPaymentCollectionMethod { get; set; }

    public bool Test { get; set; }
}
