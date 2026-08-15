using System;

namespace Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

/// <summary>
/// A request to place a hold (authorization) for an order total, paying either with raw
/// <see cref="Card"/> details or with a previously vaulted card named by <see cref="VaultId"/>.
/// Exactly one of the two must be supplied.
/// </summary>
public sealed class PayPalAuthorizeRequest
{
    public decimal Amount { get; init; }
    public string Currency { get; init; } = default!;

    /// <summary>Raw card for a one-off payment. Null when paying with a saved card.</summary>
    public CardDetails? Card { get; init; }

    /// <summary>PayPal vault token id of a saved card. Null when paying with raw card details.</summary>
    public string? VaultId { get; init; }

    /// <summary>
    /// Idempotency key sent to PayPal (PayPal-Request-Id) so a retried authorize does not
    /// create a second order/hold on the PayPal side.
    /// </summary>
    public string IdempotencyKey { get; init; } = default!;
}

/// <summary>State PayPal owns for a hold: the checkout order and the authorization it produced.</summary>
public sealed class PayPalAuthorization
{
    public string PayPalOrderId { get; init; } = default!;
    public string AuthorizationId { get; init; } = default!;
    /// <summary>PayPal authorization status wire value, e.g. CREATED, CAPTURED, VOIDED.</summary>
    public string Status { get; init; } = default!;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = default!;
    /// <summary>When the hold expires, if PayPal reported it.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }
}

/// <summary>Result of capturing an authorization, carrying PayPal's money breakdown.</summary>
public sealed class PayPalCapture
{
    public string CaptureId { get; init; } = default!;
    public string Status { get; init; } = default!;
    public decimal GrossAmount { get; init; }
    public decimal PayPalFee { get; init; }
    public decimal NetAmount { get; init; }
    public string Currency { get; init; } = default!;
}

/// <summary>Result of refunding a capture, in full or in part.</summary>
public sealed class PayPalRefund
{
    public string RefundId { get; init; } = default!;
    public string Status { get; init; } = default!;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = default!;
}

/// <summary>A safe, PCI-compliant descriptor of a vaulted card (never the full number).</summary>
public sealed class PayPalVaultedCard
{
    public string VaultId { get; init; } = default!;
    public string Brand { get; init; } = default!;
    public string Last4 { get; init; } = default!;
    /// <summary>Expiry as reported by PayPal, "YYYY-MM".</summary>
    public string? Expiry { get; init; }
}

/// <summary>A single transaction as PayPal's reporting API records it, for reconciliation.</summary>
public sealed class PayPalTransaction
{
    public string TransactionId { get; init; } = default!;
    public string? Status { get; init; }
    public decimal Amount { get; init; }
    public string? Currency { get; init; }
    public decimal? Fee { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
    public string? EventCode { get; init; }
}
