using System;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Raises a bill for an order. Carries the calendar date the bill falls due, and optional customer
/// details. What is billed comes from the order itself, so no amount or line items are accepted here.
/// </summary>
public class RaiseInvoiceRequest
{
    /// <summary>The calendar date the bill falls due (YYYY-MM-DD).</summary>
    public DateOnly DueDate { get; set; }

    /// <summary>Optional customer the bill is addressed to; defaults to the order's shopper.</summary>
    public CustomerDto? Customer { get; set; }
}

/// <summary>
/// Corrects the due date and/or customer details of a bill that has not yet been put to the shopper.
/// The amount is not correctable — it always comes from the order.
/// </summary>
public class CorrectInvoiceRequest
{
    /// <summary>New due date (YYYY-MM-DD). Omit to leave unchanged.</summary>
    public DateOnly? DueDate { get; set; }

    /// <summary>New customer details. Omit to leave unchanged.</summary>
    public CustomerDto? Customer { get; set; }
}
