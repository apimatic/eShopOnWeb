using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public record BillingAddressInput(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string CountryCode);

public record CardPaymentInput(
    string Number,
    string Expiry,
    string SecurityCode,
    string? Name,
    BillingAddressInput? BillingAddress);

public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    decimal Amount,
    DateTimeOffset? CreateTime,
    DateTimeOffset? ExpirationTime);

public record CaptureResult(
    string CaptureId,
    string Status,
    decimal Amount,
    decimal? PaypalFee,
    decimal? NetAmount);

public record RefundResult(
    string RefundId,
    string Status,
    decimal Amount);

public record VaultedCardResult(
    string PaymentTokenId,
    string? PayPalCustomerId,
    string? LastDigits,
    string? Brand,
    string? Expiry,
    string? Name);

public record PayPalTransactionRecord(
    string TransactionId,
    decimal? Amount,
    string? Currency,
    string? Status,
    DateTimeOffset? InitiationDate,
    string? ReferenceId,
    string? ReferenceIdType,
    string? InvoiceId,
    string? CustomField,
    string? EventCode,
    decimal? FeeAmount);
