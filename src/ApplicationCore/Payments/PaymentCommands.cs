using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>One line of a placed order: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>Optional ship-to address supplied when placing an order.</summary>
public record ShippingAddressInput(string Street, string City, string State, string Country, string ZipCode);

/// <summary>
/// How to pay for an order: exactly one of a one-off <see cref="Card"/> or a saved
/// <see cref="SavedPaymentMethodId"/>.
/// </summary>
public record PayOrderCommand(PayPalCardInput? Card, int? SavedPaymentMethodId);

/// <summary>A PayPal ledger row lined up against an eShop order (or not).</summary>
public record ReconciliationLine(
    string? PayPalTransactionId,
    string? PayPalStatus,
    decimal? PayPalAmount,
    string? Currency,
    int? OrderId,
    decimal? EShopAmount,
    string Match);

/// <summary>
/// The reconciliation report for a date range: PayPal's ledger lined up against eShop's orders, so a
/// transaction one side knows about and the other doesn't is visible.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EShopCaptureCount,
    IReadOnlyList<ReconciliationLine> Matched,
    IReadOnlyList<ReconciliationLine> InPayPalNotInEShop,
    IReadOnlyList<ReconciliationLine> InEShopNotInPayPal);
