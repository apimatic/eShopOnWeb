using System;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class CreateInvoiceForOrderRequest : BaseRequest
{
    /// <summary>The order to raise a bill against. Taken from the route.</summary>
    public int OrderId { get; set; }

    /// <summary>The calendar date the bill falls due.</summary>
    public DateOnly DueDate { get; set; }
}
