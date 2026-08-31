using System;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class RaiseInvoiceResponse : BaseResponse
{
    public RaiseInvoiceResponse(Guid correlationId) : base(correlationId)
    {
    }

    public RaiseInvoiceResponse()
    {
    }

    /// <summary>The provider identifier of the raised bill, so the flow can be driven end to end.</summary>
    public string InvoiceId { get; set; } = string.Empty;

    public string InvoiceNumber { get; set; } = string.Empty;
    public int OrderId { get; set; }

    /// <summary>Where the bill locally stands. A freshly raised bill starts out as Draft (not yet put to the shopper).</summary>
    public string Status { get; set; } = string.Empty;

    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
}
