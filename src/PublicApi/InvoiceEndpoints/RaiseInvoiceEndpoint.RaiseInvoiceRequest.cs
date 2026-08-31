using System;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>Raises a bill for an order. Carries the calendar date the bill falls due.</summary>
public class RaiseInvoiceRequest : BaseRequest
{
    public DateOnly DueDate { get; set; }
}
