using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Request to place an authorization hold for an order via PayPal.</summary>
public class PayPalAuthorizationRequest
{
    public decimal Amount { get; set; }

    /// <summary>Raw card for a one-off payment. Mutually exclusive with <see cref="VaultId"/>.</summary>
    public CardDetails? Card { get; set; }

    /// <summary>The PayPal vault token of a saved card. Mutually exclusive with <see cref="Card"/>.</summary>
    public string? VaultId { get; set; }

    /// <summary>External invoice id surfaced in PayPal reports; used to reconcile against eShop orders.</summary>
    public string? InvoiceId { get; set; }

    /// <summary>External id surfaced in PayPal reports (the eShop order id).</summary>
    public string? CustomId { get; set; }

    public string? Description { get; set; }

    /// <summary>Idempotency key (PayPal-Request-Id) so a double-click never authorizes twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

/// <summary>The outcome of an authorization: PayPal's order id, authorization id, status and expiry.</summary>
public class PayPalAuthorizationResult
{
    public string PayPalOrderId { get; set; } = string.Empty;
    public string AuthorizationId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; set; }
}

/// <summary>The outcome of a capture, with the fee and net proceeds PayPal reported.</summary>
public class PayPalCaptureResult
{
    public string CaptureId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal GrossAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
}

/// <summary>The outcome of re-authorizing a stale hold: a fresh authorization id, status and expiry.</summary>
public class PayPalReauthorizationResult
{
    public string AuthorizationId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; set; }
}

/// <summary>The outcome of a refund.</summary>
public class PayPalRefundResult
{
    public string RefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

/// <summary>A saved card as PayPal returns it after vaulting: the token id plus a safe descriptor.</summary>
public class VaultedCard
{
    public string VaultId { get; set; } = string.Empty;
    public string? CustomerId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
}

/// <summary>A single transaction from PayPal's own reporting, for reconciliation.</summary>
public class PayPalTransaction
{
    public string TransactionId { get; set; } = string.Empty;
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public decimal Amount { get; set; }
    public string? Currency { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
}
