using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public sealed record OrderLineInput(int CatalogItemId, int Quantity);

/// <summary>Optional shipping address for a placed order.</summary>
public sealed record ShippingAddressInput(
    string? Street, string? City, string? State, string? Country, string? ZipCode);

// --- Reconciliation report ---

/// <summary>Where a reconciliation line was seen and whether the two sides agree.</summary>
public enum ReconciliationOutcome
{
    /// <summary>Present on both sides and consistent.</summary>
    Matched = 0,

    /// <summary>PayPal reports a transaction that eShop has no record of.</summary>
    MissingInEShop = 1,

    /// <summary>eShop recorded money movement that PayPal's report does not (yet) show.</summary>
    MissingInPayPal = 2
}

public sealed record ReconciliationLine(
    ReconciliationOutcome Outcome,
    string? PayPalTransactionId,
    string? PayPalStatus,
    decimal? PayPalAmount,
    string? Currency,
    string? InvoiceId,
    int? OrderId,
    string? EShopPaymentStatus,
    decimal? EShopAmount,
    string Note);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EShopRecordCount,
    IReadOnlyList<ReconciliationLine> Matched,
    IReadOnlyList<ReconciliationLine> MissingInEShop,
    IReadOnlyList<ReconciliationLine> MissingInPayPal);
