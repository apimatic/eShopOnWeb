using System;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class CorrectInvoiceResponse : BaseResponse
{
    public CorrectInvoiceResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CorrectInvoiceResponse()
    {
    }

    public string InvoiceId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}
