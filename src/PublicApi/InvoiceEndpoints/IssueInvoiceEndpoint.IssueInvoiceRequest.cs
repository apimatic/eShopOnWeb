namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class IssueInvoiceRequest : BaseRequest
{
    public IssueInvoiceRequest(int invoiceId)
    {
        InvoiceId = invoiceId;
    }

    public int InvoiceId { get; set; }
}
