namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class WithdrawInvoiceRequest : BaseRequest
{
    public WithdrawInvoiceRequest(int invoiceId)
    {
        InvoiceId = invoiceId;
    }

    public int InvoiceId { get; set; }
}
