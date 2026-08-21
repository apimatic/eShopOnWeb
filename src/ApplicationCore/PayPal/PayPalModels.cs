using System;

namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

/// <summary>Raw card details for a one-off card payment. Never persisted by the application.</summary>
public record PayPalCardDetails
{
    /// <summary>Card number, digits only (e.g. "4111111111111111").</summary>
    public required string Number { get; init; }

    /// <summary>Expiry in "YYYY-MM" form.</summary>
    public required string Expiry { get; init; }

    public required string SecurityCode { get; init; }

    public string? CardholderName { get; init; }
}

/// <summary>
/// A request to place a hold. Either <see cref="Card"/> (raw card) or <see cref="VaultId"/> (saved
/// card) identifies how to pay — never both.
/// </summary>
public record PayPalAuthorizationRequest
{
    public required decimal Amount { get; init; }
    public required string CurrencyCode { get; init; }

    /// <summary>The eShop order id, echoed onto the PayPal order for later reconciliation.</summary>
    public required int OrderReference { get; init; }

    /// <summary>Per-payment token that makes the PayPal invoice id unique.</summary>
    public required string InvoiceReference { get; init; }

    /// <summary>Stable idempotency key so a double-click never authorizes twice.</summary>
    public required string IdempotencyKey { get; init; }

    public PayPalCardDetails? Card { get; init; }

    /// <summary>PayPal vault token id when paying with a saved card.</summary>
    public string? VaultId { get; init; }
}

/// <summary>The outcome of a hold. When <see cref="RequiresAction"/> is true, the card needs browser approval.</summary>
public record PayPalAuthorizationResult
{
    public required string PayPalOrderId { get; init; }
    public string? AuthorizationId { get; init; }
    public string? AuthorizationStatus { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>True when PayPal returned a challenge (e.g. 3-D Secure) that needs browser approval.</summary>
    public bool RequiresAction { get; init; }
}

/// <summary>The outcome of a capture, including what PayPal reported it took and kept.</summary>
public record PayPalCaptureResult
{
    public required string CaptureId { get; init; }
    public string? Status { get; init; }

    /// <summary>Gross amount captured.</summary>
    public required decimal GrossAmount { get; init; }

    /// <summary>PayPal's fee (may be null while the capture is pending).</summary>
    public decimal? PayPalFee { get; init; }

    /// <summary>Net proceeds to the merchant (may be null while the capture is pending).</summary>
    public decimal? NetAmount { get; init; }

    public required string CurrencyCode { get; init; }
}

/// <summary>The outcome of a refund.</summary>
public record PayPalRefundResult
{
    public required string RefundId { get; init; }
    public string? Status { get; init; }
    public required decimal Amount { get; init; }
    public required string CurrencyCode { get; init; }
}

/// <summary>The safe descriptor and vault id of a card that has been saved (vaulted).</summary>
public record PayPalVaultResult
{
    public required string VaultId { get; init; }
    public required string CardBrand { get; init; }
    public required string LastFourDigits { get; init; }
    public string? Expiry { get; init; }
}

/// <summary>One transaction from PayPal's transaction-search report, used for reconciliation.</summary>
public record PayPalTransaction
{
    public required string TransactionId { get; init; }
    public decimal? Amount { get; init; }
    public string? CurrencyCode { get; init; }
    public string? Status { get; init; }

    /// <summary>The invoice id PayPal recorded (the integration sets this to the eShop order reference).</summary>
    public string? InvoiceId { get; init; }

    /// <summary>The custom field PayPal recorded (the integration sets this to the eShop order id).</summary>
    public string? CustomField { get; init; }

    public DateTimeOffset? InitiationDate { get; init; }
}
