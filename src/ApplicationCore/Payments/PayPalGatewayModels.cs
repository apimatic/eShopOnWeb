using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed record PayPalMoneyDto(string CurrencyCode, string Value);

public sealed record PayPalBillingAddressInput(
    string CountryCode,
    string? AddressLine1,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode);

public sealed record PayPalCardInput(
    string? Name,
    string Number,
    string Expiry,
    string? SecurityCode,
    PayPalBillingAddressInput? BillingAddress);

public sealed record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    string? ExpirationTime,
    PayPalMoneyDto Amount);

public sealed record PayPalAuthorizationSnapshot(
    string AuthorizationId,
    string Status,
    string? ExpirationTime,
    PayPalMoneyDto? Amount);

public sealed record PayPalCaptureResult(
    string CaptureId,
    string Status,
    PayPalMoneyDto Amount,
    PayPalMoneyDto? PaypalFee,
    PayPalMoneyDto? NetAmount);

public sealed record PayPalRefundResult(
    string RefundId,
    string Status,
    PayPalMoneyDto Amount,
    PayPalMoneyDto? TotalRefundedAmount);

public sealed record PayPalVaultedCard(
    string PaymentTokenId,
    string? PayPalCustomerId,
    string? LastDigits,
    string? Brand,
    string? Expiry,
    string? Name,
    string? CardType);

public sealed record PayPalTransactionRecord(
    string? TransactionId,
    string? PaypalReferenceId,
    string? TransactionInitiationDate,
    string? TransactionUpdatedDate,
    PayPalMoneyDto? TransactionAmount,
    PayPalMoneyDto? FeeAmount,
    string? TransactionStatus,
    string? InvoiceId,
    string? CustomField);
