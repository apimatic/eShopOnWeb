using System;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class GetInvoiceResponse : BaseResponse
{
    public GetInvoiceResponse(Guid correlationId) : base(correlationId) { }

    public GetInvoiceResponse() { }

    public InvoiceDto Invoice { get; set; } = new();

    /// <summary>
    /// How the bill can be paid. Present only once the bill has been put to the shopper;
    /// withheld before it is issued and after it is withdrawn.
    /// </summary>
    public string? PaymentLink { get; set; }
}
