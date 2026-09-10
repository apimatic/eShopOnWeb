using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// The instruction describing how to fund an authorization: either raw card details for a one-off
/// payment, or the id of a previously vaulted card belonging to the shopper.
/// </summary>
public record AuthorizeInstruction(CardDetails? Card, string? VaultId)
{
    public bool UsesSavedCard => !string.IsNullOrEmpty(VaultId);
}

/// <summary>Result of placing a hold (authorization) on the buyer's funds via PayPal.</summary>
public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpiresAt,
    string? CardBrand,
    string? CardLast4);

/// <summary>Result of capturing (settling) an authorized payment. Carries what PayPal reported.</summary>
public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal PayPalFee,
    decimal NetAmount,
    string Currency,
    bool FinalCapture);

/// <summary>Result of refunding a captured payment, in full or in part.</summary>
public record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

/// <summary>Result of vaulting (saving) a card. Only safe-to-display metadata is returned.</summary>
public record PayPalVaultCardResult(
    string VaultId,
    string CustomerId,
    string? Brand,
    string? Last4,
    string? Expiry,
    string? CardholderName);

/// <summary>A single transaction as reported by PayPal's Transaction Search (reconciliation source of truth).</summary>
public record PayPalTransaction(
    string TransactionId,
    string? InvoiceId,
    string? CustomField,
    decimal Amount,
    string Currency,
    decimal? Fee,
    string Status,
    string EventCode,
    DateTimeOffset Date);
