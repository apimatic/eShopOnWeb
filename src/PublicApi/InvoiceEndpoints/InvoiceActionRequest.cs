namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>Carries the target invoice id for the operator actions (issue / withdraw).</summary>
public class InvoiceActionRequest : BaseRequest
{
    public InvoiceActionRequest(string invoiceId)
    {
        InvoiceId = invoiceId;
    }

    public InvoiceActionRequest()
    {
    }

    public string InvoiceId { get; set; } = string.Empty;
}
