using System;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class IssueInvoiceResponse : BaseResponse
{
    public IssueInvoiceResponse(Guid correlationId) : base(correlationId)
    {
    }

    public IssueInvoiceResponse()
    {
    }

    public string InvoiceId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;

    /// <summary>How the bill can now be paid, handed out because it has been put to the shopper.</summary>
    public string? PaymentLink { get; set; }
}
