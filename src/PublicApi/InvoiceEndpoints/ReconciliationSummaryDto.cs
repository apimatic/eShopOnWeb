namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>Headline counts for a reconciliation report.</summary>
public class ReconciliationSummaryDto
{
    public int ProviderInvoiceCount { get; set; }
    public int EShopInvoiceCount { get; set; }
    public int ReconciledCount { get; set; }
    public int MissingFromEShopCount { get; set; }
    public int MissingFromProviderCount { get; set; }
    public int ForeignProviderInvoiceCount { get; set; }
}
