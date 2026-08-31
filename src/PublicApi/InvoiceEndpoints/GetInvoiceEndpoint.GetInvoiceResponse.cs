using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class GetInvoiceResponse : BaseResponse
{
    public GetInvoiceResponse(Guid correlationId) : base(correlationId)
    {
    }

    public GetInvoiceResponse()
    {
    }

    public InvoiceDto Invoice { get; set; } = new();

    /// <summary>The provider's own current status string for the bill.</summary>
    public string? ProviderStatus { get; set; }

    /// <summary>How the shopper can pay the bill, once it has been put to them; otherwise null.</summary>
    public string? PaymentLink { get; set; }

    /// <summary>The provider's account of how the bill reached its current state.</summary>
    public List<InvoiceHistoryDto> History { get; set; } = new();
}
