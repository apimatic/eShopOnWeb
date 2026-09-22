using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>A line in a place-order request: a catalog item and how many.</summary>
public record PlaceOrderItem(int CatalogItemId, int Quantity);

/// <summary>Optional shipping address for a placed order (defaults are applied when absent).</summary>
public record ShippingAddressInput(string? Street, string? City, string? State, string? Country, string? ZipCode);

/// <summary>The payment state of an order, safe to return to a shopper.</summary>
public record PaymentView(
    int OrderId,
    string Status,
    string Currency,
    decimal AuthorizedAmount,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal RefundedAmount,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? CaptureId,
    string? LastError);

/// <summary>An order plus its payment state, for the caller's own order list.</summary>
public record MyOrderView(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    string Currency,
    string PaymentStatus,
    PaymentView? Payment,
    IReadOnlyList<MyOrderItemView> Items);

public record MyOrderItemView(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

/// <summary>A saved card, described safely enough to recognise — never full card details.</summary>
public record SavedCardView(
    Guid PaymentMethodId,
    string Brand,
    string LastFourDigits,
    string ExpiryMonthYear,
    string? CardholderName,
    DateTimeOffset CreatedAt);

/// <summary>One row of the reconciliation report lining a PayPal transaction up against an eShop order.</summary>
public record ReconciliationLine(
    string? PayPalTransactionId,
    string? InvoiceId,
    int? OrderId,
    decimal? PayPalAmount,
    decimal? EShopAmount,
    string? PayPalStatus,
    string? EShopStatus,
    string Classification); // "matched" | "only-in-paypal" | "only-in-eshop"

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EShopOrderCount,
    int PagesScanned,
    bool Truncated,
    IReadOnlyList<ReconciliationLine> Lines);
