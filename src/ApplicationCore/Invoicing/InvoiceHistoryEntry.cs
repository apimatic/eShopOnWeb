using System;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>
/// One entry in the provider's account of how a bill reached its current state. Transaction details are
/// only present for payment events.
/// </summary>
public record InvoiceHistoryEntry
{
    public string? Event { get; init; }
    public DateTimeOffset? Date { get; init; }
    public string? TransactionId { get; init; }
    public string? TransactionAmount { get; init; }
}
