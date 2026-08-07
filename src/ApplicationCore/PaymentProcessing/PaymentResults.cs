namespace Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

/// <summary>
/// A payment source for charging an order: either raw <see cref="Card"/> details for a
/// one-off payment, or the <see cref="VaultId"/> of a previously saved card. Exactly one is set.
/// </summary>
public record CardPaymentSource
{
    public CardDetails? Card { get; init; }
    public string? VaultId { get; init; }

    public static CardPaymentSource FromCard(CardDetails card) => new() { Card = card };
    public static CardPaymentSource FromVault(string vaultId) => new() { VaultId = vaultId };
}

/// <summary>
/// Outcome of creating and capturing a PayPal Checkout order (v2 /checkout/orders).
/// </summary>
public record CaptureResult
{
    public required string PayPalOrderId { get; init; }

    /// <summary>Capture id (purchase_units[].payments.captures[].id); null when not captured.</summary>
    public string? CaptureId { get; init; }

    /// <summary>PayPal order status, e.g. <c>COMPLETED</c> (spec: order_status).</summary>
    public required string OrderStatus { get; init; }

    /// <summary>Capture status, e.g. <c>COMPLETED</c> / <c>DECLINED</c> (spec: capture.status).</summary>
    public string? CaptureStatus { get; init; }

    /// <summary>True when the order completed and funds were captured.</summary>
    public bool IsCompleted => CaptureId is not null &&
        string.Equals(CaptureStatus, "COMPLETED", System.StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Outcome of refunding a captured payment (v2 /payments/captures/{id}/refund).
/// </summary>
public record RefundResult
{
    public required string RefundId { get; init; }

    /// <summary>Refund status, e.g. <c>COMPLETED</c> / <c>PENDING</c> (spec: refund_status).</summary>
    public required string Status { get; init; }

    public bool IsCompleted =>
        string.Equals(Status, "COMPLETED", System.StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Status, "PENDING", System.StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Safe description of a card vaulted with PayPal (v3 /vault/payment-tokens). Carries the
/// token reference plus only non-sensitive descriptors.
/// </summary>
public record VaultedCardResult
{
    public required string VaultId { get; init; }
    public required string CustomerId { get; init; }
    public string? CardBrand { get; init; }
    public string? LastDigits { get; init; }
    public string? Expiry { get; init; }
    public string? CardholderName { get; init; }
}
