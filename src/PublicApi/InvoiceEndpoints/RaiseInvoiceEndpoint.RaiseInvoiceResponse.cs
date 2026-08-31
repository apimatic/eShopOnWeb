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

    /// <summary>The identifier of the raised bill, which the operator endpoints act on.</summary>
    public string InvoiceId { get; set; } = string.Empty;

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
}
