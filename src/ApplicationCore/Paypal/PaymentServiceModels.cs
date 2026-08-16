using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Paypal;

// ---- Inputs to the application services (already stripped of any transport concern) ----

/// <summary>One line of a placed order: a catalog item and how many of it.</summary>
public record OrderLineInput(int CatalogItemId, int Quantity);

public record ShippingAddressInput(string Street, string City, string State, string Country, string ZipCode);

public record PlaceOrderInput(IReadOnlyCollection<OrderLineInput> Items, ShippingAddressInput? ShipTo);

/// <summary>
/// How to pay: exactly one of a one-off card or a reference to a saved card owned by the caller.
/// </summary>
public record PayOrderInput(PayPalCardDetails? Card, int? SavedPaymentMethodId);

public record RefundOrderInput(decimal? Amount, string IdempotencyKey);

public record SaveCardInput(PayPalCardDetails Card, string? Alias);

// ---- Reconciliation output ----

public enum ReconciliationMatch
{
    /// <summary>Present in both PayPal's records and eShop, and consistent.</summary>
    Matched = 0,

    /// <summary>PayPal knows about a transaction that eShop cannot line up to a known payment.</summary>
    InPayPalOnly = 1,

    /// <summary>eShop has a payment that did not appear in PayPal's reporting for the range.</summary>
    InEShopOnly = 2
}

public record ReconciliationLine
{
    public required ReconciliationMatch Match { get; init; }
    public int? OrderId { get; init; }
    public string? PayPalTransactionId { get; init; }
    public string? PayPalStatus { get; init; }
    public string? EventCode { get; init; }
    public decimal? PayPalAmount { get; init; }
    public decimal? EShopAmount { get; init; }
    public string? CurrencyCode { get; init; }
    public DateTimeOffset? Date { get; init; }
}

public record ReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public required int PayPalTransactionCount { get; init; }
    public required int MatchedCount { get; init; }
    public required int InPayPalOnlyCount { get; init; }
    public required int InEShopOnlyCount { get; init; }
    public required IReadOnlyList<ReconciliationLine> Lines { get; init; }
}
