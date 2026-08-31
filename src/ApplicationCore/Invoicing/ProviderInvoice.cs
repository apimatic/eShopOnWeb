using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>
/// The provider's view of a bill: its identifier there, the status it reports, how it reached that state,
/// and — once the bill has been put to the shopper — the link they can pay it with. This is an
/// anti-corruption boundary: no Visa/CyberSource SDK type crosses it.
/// </summary>
public record ProviderInvoice
{
    public required string ProviderInvoiceId { get; init; }
    public string? Status { get; init; }
    public string? PaymentLink { get; init; }
    public IReadOnlyList<InvoiceHistoryEntry> History { get; init; } = new List<InvoiceHistoryEntry>();
}
