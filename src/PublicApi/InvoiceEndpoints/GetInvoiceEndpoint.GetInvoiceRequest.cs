namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class GetInvoiceRequest : BaseRequest
{
    public GetInvoiceRequest(string invoiceId, string buyerId)
    {
        InvoiceId = invoiceId;
        BuyerId = buyerId;
    }

    public string InvoiceId { get; }
    public string BuyerId { get; }
}
