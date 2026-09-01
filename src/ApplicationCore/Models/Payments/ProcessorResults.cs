using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

/// <summary>Result of a successful authorization (hold) at the processor.</summary>
public sealed record ProcessorAuthorization(
    string ProcessorOrderId,
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpiresAt,
    string? CardBrand,
    string? CardLastDigits);

/// <summary>Current processor-side state of an authorization.</summary>
public sealed record ProcessorAuthorizationState(
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpiresAt);

/// <summary>Result of a capture, including the processor's fee breakdown.</summary>
public sealed record ProcessorCapture(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? Fee,
    decimal? NetAmount,
    string Currency,
    DateTimeOffset CapturedAt);

/// <summary>Result of a refund against a capture.</summary>
public sealed record ProcessorRefund(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency,
    decimal? TotalRefundedAmount);

/// <summary>Result of vaulting a card; only safe display metadata comes back.</summary>
public sealed record ProcessorVaultedCard(
    string VaultTokenId,
    string? CardBrand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName);

/// <summary>A single transaction line from the processor's own records (for reconciliation).</summary>
public sealed record ProcessorTransaction(
    string TransactionId,
    string? ReferenceId,
    string? ReferenceIdType,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? FeeAmount,
    string? InvoiceId,
    string? CustomField,
    DateTimeOffset? InitiatedAt,
    DateTimeOffset? UpdatedAt);
