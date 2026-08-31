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

    /// <summary>The identifier of the raised bill, so the flow can be driven end to end.</summary>
    public int InvoiceId { get; set; }

    public int OrderId { get; set; }
    public string ProviderInvoiceId { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
}
