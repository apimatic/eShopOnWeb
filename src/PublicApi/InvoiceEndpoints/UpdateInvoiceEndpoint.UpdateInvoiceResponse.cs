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

    public int InvoiceId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
}
