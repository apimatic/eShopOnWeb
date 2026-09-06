namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

internal sealed class SiteEnvelope
{
    public SiteResource? Site { get; set; }
}

internal sealed class SiteResource
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Subdomain { get; set; }
    public string? Currency { get; set; }
    public bool RelationshipInvoicingEnabled { get; set; }
    public string? DefaultPaymentCollectionMethod { get; set; }
    public bool Test { get; set; }
}
