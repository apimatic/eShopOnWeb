using System;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>Identifies the bill an operator action (issue/withdraw) targets.</summary>
public class InvoiceActionRequest : BaseRequest
{
    /// <summary>The provider invoice id (bound from the route).</summary>
    public string InvoiceId { get; set; } = string.Empty;
}

public class InvoiceActionResponse : BaseResponse
{
    public InvoiceActionResponse(Guid correlationId) : base(correlationId) { }

    public InvoiceActionResponse() { }

    public InvoiceDto Invoice { get; set; } = new();

    /// <summary>How the bill can be paid; present after it is issued and withheld after it is withdrawn.</summary>
    public string? PaymentLink { get; set; }
}
