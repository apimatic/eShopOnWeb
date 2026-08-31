using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class GetInvoiceByIdRequest : BaseRequest
{
    public GetInvoiceByIdRequest(string invoiceId)
    {
        InvoiceId = invoiceId;
    }

    public string InvoiceId { get; }
}

public class InvoiceHistoryDto
{
    public string? Event { get; set; }
    public DateTimeOffset? Date { get; set; }
}

public class GetInvoiceResponse : BaseResponse
{
    public GetInvoiceResponse(Guid correlationId) : base(correlationId) { }
    public GetInvoiceResponse() { }

    public InvoiceDto Invoice { get; set; } = new();

    /// <summary>How the shopper can pay — present only once the bill has been put to them.</summary>
    public string? PaymentLink { get; set; }

    /// <summary>The provider's own record of how the bill reached its current state.</summary>
    public List<InvoiceHistoryDto> History { get; set; } = new();
}
