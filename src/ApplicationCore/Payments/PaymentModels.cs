using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Raw card details for a one-off payment or for vaulting. Never persisted, never logged.</summary>
public record CardDetails(
    string Number,
    string ExpiryMonth,
    string ExpiryYear,
    string SecurityCode,
    string? CardholderName,
    BillingAddress? BillingAddress);

/// <summary>Billing address that accompanies a raw card.</summary>
public record BillingAddress(
    string AddressLine1,
    string? AddressLine2,
    string AdminArea2,   // city
    string? AdminArea1,  // state / province
    string CountryCode,  // ISO-3166-1 alpha-2
    string PostalCode);

/// <summary>The result of placing an authorization hold with PayPal.</summary>
public record AuthorizationResult(string PayPalOrderId, string AuthorizationId, string Status);

/// <summary>The current status of a PayPal authorization.</summary>
public record AuthorizationStatus(string AuthorizationId, string Status);

/// <summary>The result of capturing an authorization, carrying what PayPal reported.</summary>
public record CaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal PayPalFee,
    decimal NetAmount);

/// <summary>The result of renewing (reauthorizing) a stale authorization.</summary>
public record ReauthorizationResult(string AuthorizationId, string Status);

/// <summary>The result of refunding a capture, in full or in part.</summary>
public record RefundResult(string RefundId, string Status, decimal Amount);

/// <summary>The result of vaulting a card. Carries only a safe description plus the vault token id.</summary>
public record VaultCardResult(
    string VaultId,
    string Brand,
    string LastFourDigits,
    string? ExpiryMonth,
    string? ExpiryYear,
    string? Status);

/// <summary>One transaction as PayPal records it, for reconciliation.</summary>
public record PayPalTransaction(
    string TransactionId,
    string Status,
    string EventCode,
    decimal Amount,
    string Currency,
    DateTimeOffset Date,
    string? InvoiceId,
    string? CustomField);

/// <summary>A full page-through of PayPal transactions for a date range.</summary>
public record TransactionSearchResult(IReadOnlyList<PayPalTransaction> Transactions, int PagesRead);
