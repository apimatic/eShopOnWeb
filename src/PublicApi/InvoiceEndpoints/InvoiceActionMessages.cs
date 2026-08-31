using System;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>An operator action on a specific bill, identified by route.</summary>
public class InvoiceActionRequest : BaseRequest
{
    public InvoiceActionRequest(int invoiceId)
    {
        InvoiceId = invoiceId;
    }

    public int InvoiceId { get; }
}

public class InvoiceActionResponse : BaseResponse
{
    public InvoiceActionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public InvoiceActionResponse()
    {
    }

    public int InvoiceId { get; set; }

    /// <summary>The bill's stage after the action: Issued or Withdrawn.</summary>
    public string Status { get; set; } = string.Empty;
}
