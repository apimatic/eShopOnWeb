using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public record PayPalMoneyAmount(string CurrencyCode, decimal Value);

public record PayPalCardDetails(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? Name,
    PayPalBillingAddress? BillingAddress);

public record PayPalBillingAddress(
    string CountryCode,
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea1,
    string? AdminArea2,
    string? PostalCode);

public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string PayPalOrderStatus,
    string AuthorizationId,
    string AuthorizationStatus,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpiresAt);

public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal Amount,
    decimal? PaypalFee,
    decimal? NetProceeds,
    string Currency);

public record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

public record PayPalVaultedCard(
    string VaultId,
    string? CustomerId,
    string LastDigits,
    string? Brand,
    string? Expiry,
    string? CardholderName);

public record PayPalReportedTransaction(
    string TransactionId,
    string? ReferenceId,
    string? InvoiceId,
    string? CustomField,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? InitiationDate,
    decimal? FeeAmount);

public record PayPalAuthorizationDetails(
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpiresAt);
