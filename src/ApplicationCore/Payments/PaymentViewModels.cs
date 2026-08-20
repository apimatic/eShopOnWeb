using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>A line requested when placing an order.</summary>
public sealed record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>Optional shipping address supplied when placing an order.</summary>
public sealed record ShippingAddressRequest(
    string Street,
    string City,
    string State,
    string Country,
    string ZipCode);

/// <summary>
/// How to pay an order: exactly one of a raw card, or one of the shopper's saved cards (by id). The service
/// resolves a saved card to its PayPal vault token, enforcing that it belongs to the caller.
/// </summary>
public sealed class PayInstruction
{
    public CardDetails? Card { get; init; }
    public int? PaymentMethodId { get; init; }
}

public sealed record OrderLineViewModel(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

public sealed record RefundViewModel(string PayPalRefundId, decimal Amount, string Status, DateTimeOffset CreatedDate);

/// <summary>The full payment/fulfilment state of an order, returned by every order-payment endpoint.</summary>
public sealed record PaymentDetailsViewModel(
    int OrderId,
    DateTimeOffset OrderDate,
    string BuyerId,
    decimal Amount,
    string CurrencyCode,
    string Status,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal TotalRefunded,
    decimal RefundableRemaining,
    string? LastError,
    IReadOnlyList<RefundViewModel> Refunds,
    IReadOnlyList<OrderLineViewModel> Items);

public sealed record PaymentMethodViewModel(
    int Id,
    string CardBrand,
    string LastFourDigits,
    string? Expiry,
    string? CardholderName,
    DateTimeOffset CreatedDate);

/// <summary>A PayPal transaction lined up against an eShop payment record.</summary>
public sealed record ReconciliationMatch(
    string TransactionId,
    string? PayPalStatus,
    decimal? PayPalAmount,
    int OrderId,
    string EShopKind,
    decimal EShopAmount,
    string EShopStatus);

/// <summary>An eShop payment record (a capture or a refund) that PayPal's report did not show for the range.</summary>
public sealed record ReconciliationEShopEntry(
    int OrderId,
    string Kind,
    string Reference,
    decimal Amount,
    string Status);

/// <summary>
/// A reconciliation report over a date range: PayPal's own transactions lined up against eShop's records, so
/// a payment one side knows about and the other does not is visible either way.
/// </summary>
public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<PayPalTransactionRecord> OnlyInPayPal,
    IReadOnlyList<ReconciliationEShopEntry> OnlyInEShop);
