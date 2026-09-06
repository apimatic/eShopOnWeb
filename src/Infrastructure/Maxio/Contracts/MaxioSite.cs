namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

/// <summary>Maxio site metadata. Read for the site currency so prices can be rendered correctly.</summary>
public class MaxioSite
{
    public long Id { get; set; }

    public string? Name { get; set; }

    public string? Subdomain { get; set; }

    public string? Currency { get; set; }

    /// <summary>
    /// True on the current Relationship Invoicing architecture, where non-automatic collection is
    /// called "remittance"; false on legacy Statements, where it is called "invoice".
    /// </summary>
    public bool RelationshipInvoicingEnabled { get; set; }

    public string? DefaultPaymentCollectionMethod { get; set; }

    /// <summary>True for sandbox/test sites.</summary>
    public bool Test { get; set; }
}
