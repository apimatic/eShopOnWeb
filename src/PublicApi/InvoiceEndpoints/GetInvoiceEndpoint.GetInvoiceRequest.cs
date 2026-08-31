namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class GetInvoiceRequest : BaseRequest
{
    public GetInvoiceRequest(string invoiceId)
    {
        InvoiceId = invoiceId;
    }

    public GetInvoiceRequest()
    {
    }

    public string InvoiceId { get; set; } = string.Empty;
}
