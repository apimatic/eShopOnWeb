using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The card a shopper is paying with, or saving. Exactly one of the two forms is populated:
/// a one-off card (<see cref="Number"/>+<see cref="Expiry"/>+<see cref="SecurityCode"/>), or a
/// previously-saved card referenced by its vault token (<see cref="VaultId"/>). Card numbers flow
/// through this record straight to PayPal and are never persisted or logged.
/// </summary>
public record CardPaymentInput
{
    public string? Number { get; init; }

    /// <summary>Expiry in ISO-8601 <c>YYYY-MM</c> form, as PayPal expects.</summary>
    public string? Expiry { get; init; }

    public string? SecurityCode { get; init; }
    public string? CardholderName { get; init; }

    /// <summary>PayPal vault token id when paying with a saved card.</summary>
    public string? VaultId { get; init; }

    public bool IsVaulted => !string.IsNullOrEmpty(VaultId);
}

/// <summary>The hold PayPal placed on the shopper's money.</summary>
public record PayPalAuthorizationResult(string PayPalOrderId, string AuthorizationId, string Status);

/// <summary>An authorization reference after a (re)authorization.</summary>
public record PayPalAuthorizationRef(string AuthorizationId, string Status);

/// <summary>The money actually taken at capture, as PayPal reported it.</summary>
public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount);

/// <summary>A refund PayPal processed against a capture.</summary>
public record PayPalRefundResult(string RefundId, string Status, decimal Amount);

/// <summary>A card PayPal vaulted, described safely (never the full number).</summary>
public record PayPalVaultResult(
    string VaultTokenId,
    string? CustomerId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName);

/// <summary>One transaction as PayPal's transaction reporting knows it.</summary>
public record PayPalTransaction(
    string? TransactionId,
    string? Status,
    decimal? Amount,
    string? CurrencyCode,
    string? InvoiceId,
    DateTimeOffset? InitiationDate);
