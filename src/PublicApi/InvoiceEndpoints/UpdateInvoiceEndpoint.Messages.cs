using System;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class UpdateInvoiceRequest : BaseRequest
{
    /// <summary>Set from the route, not the body.</summary>
    public string InvoiceId { get; set; } = string.Empty;

    /// <summary>The corrected due date, if being changed.</summary>
    public DateOnly? DueDate { get; set; }

    /// <summary>The corrected customer details, if being changed.</summary>
    public InvoiceCustomerRequest? Customer { get; set; }
}

public class UpdateInvoiceResponse : BaseResponse
{
    public UpdateInvoiceResponse(Guid correlationId) : base(correlationId) { }
    public UpdateInvoiceResponse() { }

    public InvoiceDto Invoice { get; set; } = new();
}
