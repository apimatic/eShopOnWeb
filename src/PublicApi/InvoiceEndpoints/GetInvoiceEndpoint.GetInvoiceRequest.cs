namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class GetInvoiceRequest : BaseRequest
{
    /// <summary>The provider invoice id (bound from the route).</summary>
    public string InvoiceId { get; set; } = string.Empty;
}
