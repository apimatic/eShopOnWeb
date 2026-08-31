using System;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Raises a bill for an order. What is billed comes from the order itself; the caller
/// supplies only the calendar date the bill falls due.
/// </summary>
public class CreateInvoiceForOrderRequest : BaseRequest
{
    /// <summary>The order to bill (bound from the route).</summary>
    public int OrderId { get; set; }

    /// <summary>The calendar date the bill falls due.</summary>
    public DateOnly DueDate { get; set; }
}
