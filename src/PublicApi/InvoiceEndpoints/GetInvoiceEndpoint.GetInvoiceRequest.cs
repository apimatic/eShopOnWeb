namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class GetInvoiceRequest : BaseRequest
{
    public GetInvoiceRequest(int invoiceId)
    {
        InvoiceId = invoiceId;
    }

    public int InvoiceId { get; set; }
}
