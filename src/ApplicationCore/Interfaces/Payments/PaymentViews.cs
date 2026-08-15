using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>One line of a placed order: a catalog item id and how many.</summary>
public sealed record OrderLineInput(int CatalogItemId, int Quantity);

/// <summary>Optional shipping address for a placed order.</summary>
public sealed record ShippingAddressInput(string Street, string City, string State, string Country, string ZipCode);

/// <summary>
/// How to pay: either a one-off <see cref="Card"/> or a saved card by <see cref="SavedPaymentMethodId"/>.
/// Exactly one must be provided.
/// </summary>
public sealed record AuthorizeInstruction(CardDetails? Card, int? SavedPaymentMethodId);

/// <summary>A refund line within a payment view.</summary>
public sealed record RefundView(int Id, string RefundId, decimal Amount, string Status);

/// <summary>
/// A shopper- and operator-facing view of an order and everything PayPal owns about its payment.
/// </summary>
public sealed record OrderPaymentView(
    int OrderId,
    string Status,
    decimal Total,
    string CurrencyCode,
    DateTimeOffset OrderDate,
    IReadOnlyList<OrderLineView> Items,
    PaymentView? Payment);

public sealed record OrderLineView(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

public sealed record PaymentView(
    string PaymentStatus,
    string? InstrumentDescription,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal TotalRefunded,
    decimal RefundableRemaining,
    IReadOnlyList<RefundView> Refunds);
