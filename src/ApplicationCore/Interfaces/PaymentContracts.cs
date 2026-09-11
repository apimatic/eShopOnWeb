using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A catalog item and quantity requested when placing an order.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>Optional shipping address supplied when placing an order.</summary>
public record ShippingAddressRequest(string Street, string City, string State, string Country, string ZipCode);

/// <summary>
/// How to fund a payment: exactly one of a one-off <see cref="Card"/> or a shopper's saved
/// card (<see cref="SavedCardId"/>).
/// </summary>
public record PaymentInstruction(PayPalCard? Card, int? SavedCardId);

/// <summary>One line of a reconciliation report: a PayPal transaction lined up against an eShop order (if any).</summary>
public record ReconciliationEntry(
    string? TransactionId,
    string? InvoiceId,
    decimal? PayPalAmount,
    string? PayPalStatus,
    DateTimeOffset? TransactionDate,
    int? OrderId,
    decimal? OrderAmount,
    string? OrderState,
    string Status); // MATCHED | IN_PAYPAL_NOT_ESHOP | IN_ESHOP_NOT_PAYPAL

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationEntry> Entries);
