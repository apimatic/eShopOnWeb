using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>
/// Everything the provider needs to raise a draft bill. Built from the order (its items and what they
/// cost), never from anything a caller restates.
/// </summary>
public record RaiseInvoiceCommand
{
    public required string Description { get; init; }
    public required decimal TotalAmount { get; init; }
    public required string Currency { get; init; }
    public required DateTimeOffset DueDate { get; init; }
    public required string CustomerName { get; init; }
    public required string CustomerEmail { get; init; }

    /// <summary>A merchant reference stamped on the provider invoice (the eShopOnWeb order id).</summary>
    public string? InvoiceNumber { get; init; }

    public IReadOnlyList<InvoiceLineItemDetail> LineItems { get; init; } = new List<InvoiceLineItemDetail>();
}
