using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>
/// A provider-neutral view of a bill as the invoicing provider currently reports it. Returned by
/// <see cref="IInvoiceProvider"/> so that no provider SDK type leaks past the infrastructure layer.
/// </summary>
public record ProviderInvoice
{
    /// <summary>The provider's identifier for the bill.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>The provider-owned status (e.g. DRAFT, SENT, PARTIAL, PAID, CANCELED).</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>The way to pay the bill, present only once the provider considers it payable.</summary>
    public string? PaymentLink { get; init; }

    public DateOnly? DueDate { get; init; }

    /// <summary>When the provider recorded the bill as raised (earliest history event).</summary>
    public DateTimeOffset? CreatedDate { get; init; }

    public decimal? TotalAmount { get; init; }

    public string? Currency { get; init; }

    public string? CustomerName { get; init; }

    public string? CustomerEmail { get; init; }

    /// <summary>Whatever the provider reports about how the bill reached its current state.</summary>
    public IReadOnlyList<ProviderInvoiceEvent> History { get; init; } = Array.Empty<ProviderInvoiceEvent>();
}

/// <summary>A single step in the provider's record of how a bill reached its current state.</summary>
public record ProviderInvoiceEvent(string Event, DateTimeOffset? Date);
