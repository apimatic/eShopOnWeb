using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>
/// The application-facing shapes exchanged with the invoicing provider. They are deliberately independent
/// of the SDK's own model types so the rest of the app never depends on provider wire shapes.
/// </summary>
public record ProviderLineItem(string ProductName, int Quantity, string UnitPrice, string TotalAmount);

/// <summary>What is needed to raise a draft bill with the provider. Amounts are provider-formatted strings.</summary>
public record ProviderInvoiceDraft(
    string Description,
    DateTimeOffset DueDate,
    string CurrencyCode,
    string TotalAmount,
    string? CustomerName,
    string? CustomerEmail,
    IReadOnlyList<ProviderLineItem> LineItems);

/// <summary>The correctable fields of a bill. The amount is re-sent unchanged (the provider requires it).</summary>
public record ProviderInvoiceUpdate(
    string Description,
    DateTimeOffset DueDate,
    string CurrencyCode,
    string TotalAmount,
    string? CustomerName,
    string? CustomerEmail);

/// <summary>One step in how a bill reached its current state, as the provider reports it.</summary>
public record ProviderInvoiceEvent(string? Event, DateTimeOffset? Date);

/// <summary>
/// A bill's current state as the provider reports it — its id there, where it stands, how it can be paid
/// (once put to the shopper), and how it got there.
/// </summary>
public record ProviderInvoiceState(
    string ProviderInvoiceId,
    string? Status,
    string? PaymentLink,
    IReadOnlyList<ProviderInvoiceEvent> History);

/// <summary>
/// The provider's own summary of one bill, as returned by its list endpoint. Used for reconciliation.
/// <paramref name="CreatedDate"/> is the provider's raw string (its format is the provider's, not typed).
/// </summary>
public record ProviderInvoiceSummary(
    string ProviderInvoiceId,
    string? Status,
    string? TotalAmount,
    string? CurrencyCode,
    string? CreatedDate,
    DateTimeOffset? CreatedDateParsed);
