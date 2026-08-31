using System;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class CreateInvoiceForOrderResponse : BaseResponse
{
    public CreateInvoiceForOrderResponse(Guid correlationId) : base(correlationId) { }

    public CreateInvoiceForOrderResponse() { }

    /// <summary>The provider's identifier for the raised bill.</summary>
    public string InvoiceId { get; set; } = string.Empty;

    public InvoiceDto Invoice { get; set; } = new();
}
