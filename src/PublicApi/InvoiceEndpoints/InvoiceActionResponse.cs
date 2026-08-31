using System;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>The bill after an operator action (issue / withdraw).</summary>
public class InvoiceActionResponse : BaseResponse
{
    public InvoiceActionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public InvoiceActionResponse()
    {
    }

    public InvoiceDto Invoice { get; set; } = new();
}
