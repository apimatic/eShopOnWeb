using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>The payment/fulfilment state of an order, safe to return to a caller.</summary>
public record PaymentView
{
    public required int OrderId { get; init; }
    public required string Status { get; init; }
    public required string CurrencyCode { get; init; }
    public required decimal Amount { get; init; }

    public string? PayPalOrderId { get; init; }
    public string? AuthorizationId { get; init; }
    public string? AuthorizationStatus { get; init; }
    public DateTimeOffset? AuthorizationExpiresAt { get; init; }

    public string? CaptureId { get; init; }
    public decimal? CapturedAmount { get; init; }
    public decimal? PayPalFee { get; init; }
    public decimal? NetProceeds { get; init; }

    public decimal TotalRefunded { get; init; }
    public decimal RefundableRemaining { get; init; }
    public IReadOnlyList<RefundView> Refunds { get; init; } = Array.Empty<RefundView>();
}

public record RefundView
{
    public required string RefundId { get; init; }
    public required decimal Amount { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public record OrderLineView
{
    public required int CatalogItemId { get; init; }
    public required string ProductName { get; init; }
    public required decimal UnitPrice { get; init; }
    public required int Units { get; init; }
}

/// <summary>An order with its payment state, for GET /api/my-orders.</summary>
public record OrderPaymentView
{
    public required int OrderId { get; init; }
    public required DateTimeOffset OrderDate { get; init; }
    public required decimal Total { get; init; }
    public required IReadOnlyList<OrderLineView> Items { get; init; }
    public required PaymentView Payment { get; init; }
}

/// <summary>Details of a saved card, safe to show the shopper (never full card details).</summary>
public record SavedCardView
{
    public required int PaymentMethodId { get; init; }
    public string? Brand { get; init; }
    public string? LastDigits { get; init; }
    public string? Expiry { get; init; }
    public string? CardholderName { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
