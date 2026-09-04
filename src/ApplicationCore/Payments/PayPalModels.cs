using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Which card funding to use when authorizing an order.</summary>
public class PayPalCardSource
{
    /// <summary>One-off payment with full card details.</summary>
    public CardDetails? Card { get; init; }

    /// <summary>Pay with a previously vaulted card (the PayPal payment-token id).</summary>
    public string? VaultId { get; init; }

    /// <summary>The app's saved-card row id, when paying with a saved card.</summary>
    public int? SavedCardId { get; init; }

    public bool IsSavedCard => !string.IsNullOrEmpty(VaultId);
}

public sealed record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string AuthorizationStatus,
    DateTimeOffset? ExpirationTime);

public sealed record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string Currency);

public sealed record PayPalVoidResult(string AuthorizationId, string Status);

public sealed record PayPalReauthorizeResult(string AuthorizationId, string Status, DateTimeOffset? ExpirationTime);

public sealed record PayPalRefundResult(string RefundId, string Status, decimal Amount, string Currency);

public sealed record PayPalSetupTokenResult(string SetupTokenId, string CustomerId, string Status);

public sealed record PayPalPaymentTokenResult(
    string PaymentTokenId,
    string CustomerId,
    string Last4,
    string Brand,
    string Expiry,
    string Name);

/// <summary>A transaction row from PayPal's reporting API.</summary>
public sealed record PayPalTransaction(
    string TransactionId,
    string EventCode,
    string Status,
    DateTimeOffset InitiationDate,
    string? CustomField,
    string? InvoiceId,
    decimal? Amount,
    decimal? Fee,
    string? Currency,
    string? PayPalReferenceId,
    string? PayPalReferenceIdType);