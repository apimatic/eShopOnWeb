using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

/// <summary>Class of failure a payment operation surfaced, mapped to an HTTP status at the API boundary.</summary>
public enum PaymentFailureKind
{
    /// <summary>Provider rejected the request content (declined card, validation, limits).</summary>
    ProviderRejected = 1,
    /// <summary>Provider does not know the referenced resource.</summary>
    ResourceNotFound = 2,
    /// <summary>State conflict (already captured/voided, terminal order state).</summary>
    Conflict = 3,
    /// <summary>Provider could not be reached; nothing was sent.</summary>
    Unreachable = 4,
    /// <summary>A write may or may not have reached the provider; outcome must be settled by re-reading state.</summary>
    OutcomeUnknown = 5,
    /// <summary>Provider answered with a body that could not be processed.</summary>
    ProtocolError = 6,
    /// <summary>Merchant credentials were rejected by the provider.</summary>
    AuthenticationFailed = 7
}

/// <summary>Raw card details for a one-off payment or for vaulting. Never persisted, never logged.</summary>
public sealed record CardCredential(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? CardholderName,
    CardBillingAddress? BillingAddress);

public sealed record CardBillingAddress(
    string CountryCode,
    string? Street,
    string? City,
    string? PostalCode);

/// <summary>What funds an authorization: raw card details (one-off) or a vaulted token.</summary>
public sealed record GatewayAuthorizeSource(
    CardCredential? Card,
    string? VaultTokenId,
    string? PreviousNetworkTransactionReference);

/// <summary>Everything needed to place a hold for an exact amount, with a raw card or a vaulted token.</summary>
public sealed record GatewayAuthorizeRequest(
    decimal Amount,
    string Currency,
    string InvoiceReference,
    string CustomReference,
    GatewayAuthorizeSource Source);

public sealed record GatewayAuthorization(
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpirationTime,
    DateTimeOffset? CreatedTime,
    string? NetworkTransactionReference,
    string? ProviderOrderId);

public sealed record GatewayCapture(
    string CaptureId,
    string Status,
    string? StatusReason,
    decimal GrossAmount,
    decimal? FeeAmount,
    decimal? NetAmount,
    string Currency,
    string? AuthorizationId,
    string? ProviderOrderId);

public sealed record GatewayRefund(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency,
    decimal? TotalRefundedAmount,
    string? CaptureId,
    string? ProviderOrderId,
    string? InvoiceReference = null);

/// <summary>Provider-side view of a checkout order: which authorizations/captures/refunds it carries.</summary>
public sealed record GatewayOrderSnapshot(
    string ProviderOrderId,
    string Status,
    IReadOnlyList<GatewayAuthorization> Authorizations,
    IReadOnlyList<GatewayCapture> Captures,
    IReadOnlyList<GatewayRefund> Refunds);

/// <summary>A card stored in the provider vault, described only by token ids and display-safe fields.</summary>
public sealed record SavedVaultCard(
    string TokenId,
    string? VaultCustomerId,
    string? MerchantCustomerId,
    string? Brand,
    string? Last4,
    string? Expiry,
    string? CardholderName);

/// <summary>One row of the provider's own transaction record for the reconciliation report.</summary>
public sealed record GatewayTransaction(
    string TransactionId,
    string? TransactionStatus,
    string? TransactionEventCode,
    decimal? Amount,
    decimal? FeeAmount,
    decimal? NetAmount,
    string? Currency,
    string? InvoiceId,
    string? CustomField,
    string? PaypalReferenceId,
    string? PaypalReferenceIdType,
    string? PaymentMethodType,
    DateTimeOffset? TransactionInitiationDate,
    DateTimeOffset? TransactionUpdatedDate);
