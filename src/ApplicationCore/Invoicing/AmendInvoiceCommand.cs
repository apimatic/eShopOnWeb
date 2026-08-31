using System;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>
/// A correction to a draft bill's due date and customer details. The amount still comes from the order,
/// so it is re-sent unchanged rather than corrected.
/// </summary>
public record AmendInvoiceCommand
{
    public required string Description { get; init; }
    public required decimal TotalAmount { get; init; }
    public required string Currency { get; init; }
    public required DateTimeOffset DueDate { get; init; }
    public required string CustomerName { get; init; }
    public required string CustomerEmail { get; init; }
}
