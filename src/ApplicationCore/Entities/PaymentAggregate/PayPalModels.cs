using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public record CardBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string CountryCode);

public record CardPaymentSource(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? Name,
    CardBillingAddress? BillingAddress);

public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string PayPalOrderStatus,
    string AuthorizationId,
    string AuthorizationStatus,
    DateTimeOffset? ExpirationTime,
    DateTimeOffset? CreateTime,
    decimal Amount,
    string Currency);

public record PayPalAuthorizationSnapshot(
    string Id,
    string Status,
    DateTimeOffset? ExpirationTime,
    DateTimeOffset? CreateTime,
    decimal? Amount,
    string? Currency);

public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string Currency);

public record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

public record PayPalVaultResult(
    string VaultId,
    string? LastDigits,
    string? Brand,
    string? Expiry,
    string? Name,
    string? PayPalCustomerId);

public record PayPalReportedTransaction(
    string TransactionId,
    string? ReferenceId,
    string? ReferenceIdType,
    string? EventCode,
    string? Status,
    string? InvoiceId,
    string? CustomField,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? InitiationDate,
    decimal? FeeAmount);
