using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>Raw card details for a one-off payment or for vaulting. Never persisted or logged by this app.</summary>
public record PayPalCardDetails(
    string Number,
    string Expiry,          // "YYYY-MM"
    string SecurityCode,
    string Name,
    PayPalBillingAddress? BillingAddress);

public record PayPalBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,     // city
    string? AdminArea1,     // state
    string? PostalCode,
    string? CountryCode);

/// <summary>A request to hold (authorize) an order total with either a raw card or a vaulted card.</summary>
public record PayPalAuthorizeRequest(
    decimal Amount,
    string Currency,
    string CustomId,        // eShop order id, echoed back by reporting for reconciliation
    string RequestId,       // PayPal-Request-Id (idempotency)
    PayPalCardDetails? Card,
    string? VaultId);

public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    string? CardBrand,
    string? CardLast4);

public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal PayPalFee,
    decimal NetAmount,
    string Currency);

public record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

public record PayPalVaultCardResult(
    string VaultId,
    string? CustomerId,
    string Brand,
    string Last4,
    string Expiry,
    string? CardholderName);

/// <summary>One PayPal-side transaction row from the reporting API, used to reconcile against eShop orders.</summary>
public record PayPalTransaction(
    string TransactionId,
    string? EventCode,
    string? Status,
    decimal? Amount,
    decimal? Fee,
    string? Currency,
    DateTimeOffset? Date,
    string? CustomField,
    string? InvoiceId,
    string? ReferenceId);
