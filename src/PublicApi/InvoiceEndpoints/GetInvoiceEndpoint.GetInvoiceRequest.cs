namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class GetInvoiceRequest : BaseRequest
{
    public GetInvoiceRequest(int invoiceId, string buyerId)
    {
        InvoiceId = invoiceId;
        BuyerId = buyerId;
    }

    public int InvoiceId { get; }
    public string BuyerId { get; }
}
