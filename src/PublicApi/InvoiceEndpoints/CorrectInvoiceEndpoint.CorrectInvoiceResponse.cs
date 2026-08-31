using System;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class CorrectInvoiceResponse : BaseResponse
{
    public CorrectInvoiceResponse(Guid correlationId) : base(correlationId) { }

    public CorrectInvoiceResponse() { }

    public InvoiceDto Invoice { get; set; } = new();
}
