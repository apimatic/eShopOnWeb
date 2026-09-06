namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

/// <summary>Wire model for the specification's <c>Site Response</c> schema.</summary>
public class MaxioSiteResponse
{
    public MaxioSite? Site { get; set; }
}

/// <summary>Wire model for the specification's <c>Site</c> schema (only the fields this integration uses).</summary>
public class MaxioSite
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Subdomain { get; set; }

    /// <summary>ISO 4217 currency the site bills in.</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// True when the site runs on the Relationship Invoicing architecture, which decides the set of
    /// valid <c>payment_collection_method</c> values.
    /// </summary>
    public bool RelationshipInvoicingEnabled { get; set; }

    public string? DefaultPaymentCollectionMethod { get; set; }

    public bool Test { get; set; }
}
