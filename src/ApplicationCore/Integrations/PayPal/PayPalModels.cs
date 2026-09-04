using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Integrations.PayPal;

/// <summary>
/// Raw card data supplied by the caller for a one-off payment or to save a card.
/// Instances of this type are transient request data: they are sent to PayPal over TLS
/// and are never persisted or logged.
/// </summary>
public record CardDetails
{
    public required string Number { get; init; }
    /// <summary>Expiry in PayPal's YYYY-MM format.</summary>
    public required string Expiry { get; init; }
    public string? Cvv { get; init; }
    public string? CardHolderName { get; init; }
    public CardBillingAddress? BillingAddress { get; init; }
}

public record CardBillingAddress
{
    public string? Street { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public string? CountryCode { get; init; }
}

/// <summary>Result of creating an authorization (hold) on PayPal.</summary>
public class PayPalAuthorizationResult
{
    public required string PayPalOrderId { get; init; }
    public required string AuthorizationId { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset? ExpirationTime { get; init; }
}

/// <summary>Current status of an authorization, fetched from PayPal.</summary>
public class PayPalAuthorizationStatus
{
    public required string Id { get; init; }
    /// <summary>CREATED, COMPLETED, CAPTURED, PENDING, DENIED, VOIDED, PARTIALLY_CAPTURED...</summary>
    public required string Status { get; init; }
    public DateTimeOffset? ExpirationTime { get; init; }
}

/// <summary>Result of capturing an authorized payment.</summary>
public class PayPalCaptureResult
{
    public required string CaptureId { get; init; }
    public required string Status { get; init; }
    public required decimal CapturedAmount { get; init; }
    /// <summary>PayPal's processing fee for the capture, when reported.</summary>
    public decimal? FeeAmount { get; init; }
    /// <summary>Net proceeds to the merchant, when reported.</summary>
    public decimal? NetAmount { get; init; }
    public required string Currency { get; init; }
}

/// <summary>Result of voiding an authorization.</summary>
public class PayPalVoidResult
{
    public required string AuthorizationId { get; init; }
    public required string Status { get; init; }
}

/// <summary>Result of refunding a captured payment.</summary>
public class PayPalRefundResult
{
    public required string RefundId { get; init; }
    public required string Status { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
}

/// <summary>Result of vaulting (saving) a card at PayPal.</summary>
public class PayPalVaultResult
{
    public required string VaultId { get; init; }
    public string? Brand { get; init; }
    public string? Last4 { get; init; }
    public string? Expiry { get; init; }
}

/// <summary>A single transaction row from PayPal's transaction reporting.</summary>
public class PayPalTransactionRecord
{
    public required string TransactionId { get; init; }
    /// <summary>PayPal reference id - for Orders API payments this is the PayPal order id (ODR) or a transaction id.</summary>
    public string? PayPalReferenceId { get; init; }
    public string? PayPalReferenceIdType { get; init; }
    /// <summary>e.g. T0007 (payment), T0008 (refund), T0002 (web accept).</summary>
    public string? TransactionEventCode { get; init; }
    /// <summary>S (success), D (denied), P (pending), V (vendor credit)...</summary>
    public string? TransactionStatus { get; init; }
    public decimal Amount { get; init; }
    public decimal? FeeAmount { get; init; }
    public string? Currency { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
    public string? InvoiceId { get; init; }
    public string? PayerEmail { get; init; }
    public string? TransactionSubject { get; init; }
}

/// <summary>One page of PayPal transaction reporting results.</summary>
public class PayPalTransactionPage
{
    public required IReadOnlyList<PayPalTransactionRecord> Transactions { get; init; }
    public int Page { get; init; }
    public int TotalPages { get; init; }
    public int TotalItems { get; init; }
}
