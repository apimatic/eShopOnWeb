using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payment;

public record PayPalMoney(string Currency, decimal Value);

public record PayPalOrderResult(
    string Id,
    string Status,
    IReadOnlyList<PayPalLink> Links,
    IReadOnlyList<PayPalAuthorizationResult> Authorizations,
    IReadOnlyList<PayPalCaptureResult> Captures);

public record PayPalLink(string Rel, string Href);

public record PayPalAuthorizationResult(
    string Id,
    string Status,
    PayPalMoney? Amount,
    DateTimeOffset? CreateTime,
    DateTimeOffset? ExpirationTime,
    string? StatusReason);

public record PayPalCaptureResult(
    string Id,
    string Status,
    PayPalMoney? Amount,
    PayPalMoney? PaypalFee,
    PayPalMoney? NetAmount,
    DateTimeOffset? CreateTime);

public record PayPalRefundResult(
    string Id,
    string Status,
    PayPalMoney? Amount,
    DateTimeOffset? CreateTime);

public record PayPalVaultedCardResult(
    string VaultId,
    string? CustomerId,
    string? LastDigits,
    string? Brand,
    string? Expiry,
    string? CardholderName);

public record PayPalReportedTransaction(
    string TransactionId,
    string? ReferenceId,
    string? ReferenceIdType,
    string? EventCode,
    string? Status,
    string? InvoiceId,
    string? CustomField,
    PayPalMoney? Amount,
    PayPalMoney? FeeAmount,
    DateTimeOffset? InitiationDate);

public record CardPaymentSource(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? Name,
    CardBillingAddress? BillingAddress);

public record CardBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string CountryCode);

public record CreateAuthorizedOrderCommand(
    string InvoiceId,
    string CustomId,
    decimal Amount,
    string Currency,
    string IdempotencyKey,
    CardPaymentSource? Card,
    string? VaultId);
