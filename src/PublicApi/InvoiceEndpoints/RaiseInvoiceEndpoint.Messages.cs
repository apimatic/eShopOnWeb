using System;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class RaiseInvoiceRequest : BaseRequest
{
    /// <summary>Set from the route, not the body.</summary>
    public int OrderId { get; set; }

    /// <summary>The calendar date the bill falls due.</summary>
    public DateOnly DueDate { get; set; }

    /// <summary>Optional customer details; the shopper's own identity is used when omitted.</summary>
    public InvoiceCustomerRequest? Customer { get; set; }
}

public class InvoiceCustomerRequest
{
    public string? Name { get; set; }
    public string? Email { get; set; }
}

public class RaiseInvoiceResponse : BaseResponse
{
    public RaiseInvoiceResponse(Guid correlationId) : base(correlationId) { }
    public RaiseInvoiceResponse() { }

    /// <summary>The identifier of the raised bill.</summary>
    public string InvoiceId { get; set; } = string.Empty;

    public InvoiceDto Invoice { get; set; } = new();
}
