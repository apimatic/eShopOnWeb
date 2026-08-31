using System;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class UpdateInvoiceResponse : BaseResponse
{
    public UpdateInvoiceResponse(Guid correlationId) : base(correlationId)
    {
    }

    public UpdateInvoiceResponse()
    {
    }

    public InvoiceDto Invoice { get; set; } = new();
}
