using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public enum AuthorizationOutcome
{
    /// <summary>Funds are held and can be captured.</summary>
    Approved,
    /// <summary>Held, but the provider still reviews it; capture may still be attempted.</summary>
    Pending,
    Denied,
    Captured,
    Voided,
    /// <summary>Any other provider state in which the hold no longer exists (e.g. expired).</summary>
    NotCapturable
}

public enum CaptureOutcome
{
    Completed,
    Pending,
    Failed,
    PartiallyRefunded,
    Refunded
}

public enum RefundOutcome
{
    Completed,
    Pending,
    Failed
}

public sealed record AuthorizePaymentCommand(
    int OrderId,
    string RequestId,
    decimal Amount,
    string InvoiceId,
    string CustomId,
    string Description,
    CardDetails? Card,
    string? SavedCardToken)
{
    public override string ToString() =>
        $"Authorize order {OrderId} amount {Amount} request {RequestId} via {(Card is null ? "saved card" : Card.ToString())}";
}

public sealed record ProviderAuthorization(
    string ProviderOrderId,
    string AuthorizationId,
    AuthorizationOutcome Outcome,
    string ProviderStatus,
    decimal? Amount,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ExpiresAt,
    string? CardBrand,
    string? CardLastDigits);

public sealed record ProviderAuthorizationState(
    string AuthorizationId,
    AuthorizationOutcome Outcome,
    string ProviderStatus,
    decimal? Amount,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ExpiresAt);

public sealed record ProviderCapture(
    string CaptureId,
    CaptureOutcome Outcome,
    string ProviderStatus,
    decimal? Amount,
    decimal? PayPalFee,
    decimal? NetAmount);

public sealed record ProviderRefund(
    string RefundId,
    RefundOutcome Outcome,
    string ProviderStatus,
    decimal? Amount);

public sealed record ProviderSavedCard(
    string PaymentTokenId,
    string? ProviderCustomerId,
    string? Brand,
    string? LastDigits,
    string? Expiry);

public sealed record ProviderTransaction(
    string TransactionId,
    string? EventCode,
    string? Status,
    DateTimeOffset? InitiatedAt,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    string? InvoiceId,
    string? CustomField,
    string? ReferenceId);

public sealed record ProviderTransactionPage(
    IReadOnlyList<ProviderTransaction> Transactions,
    int Page,
    int TotalPages);
