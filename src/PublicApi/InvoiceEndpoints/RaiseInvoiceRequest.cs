using System;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Raises a bill against an order. What is billed comes from the order itself, so only the due date and
/// (optionally) the customer details the bill should carry are supplied here.
/// </summary>
public class RaiseInvoiceRequest : BaseRequest
{
    /// <summary>The order the bill is raised against. Bound from the route, not the body.</summary>
    public int OrderId { get; set; }

    /// <summary>The calendar date the bill falls due (e.g. 2026-09-30).</summary>
    public DateOnly DueDate { get; set; }

    /// <summary>Optional customer name for the bill. Defaults to the shopper.</summary>
    public string? CustomerName { get; set; }

    /// <summary>Optional customer email for the bill. Defaults to the shopper's account.</summary>
    public string? CustomerEmail { get; set; }
}
