using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

public record PayPalMoney(string CurrencyCode, decimal Value);

public record CardAuthorizationRequest(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? Name,
    CardBillingAddress? BillingAddress);

public record CardBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea1,
    string? AdminArea2,
    string? PostalCode,
    string? CountryCode);

public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string AuthorizationStatus,
    PayPalMoney Amount,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ExpiresAt);

public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal PayPalFee,
    decimal NetProceeds,
    string CurrencyCode);

public record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string CurrencyCode);

public record PayPalVaultedCard(
    string PaymentTokenId,
    string? CustomerId,
    string LastDigits,
    string? Brand,
    string? Expiry,
    string? CardholderName);

public record PayPalReportedTransaction(
    string TransactionId,
    string? PaypalReferenceId,
    string? InvoiceId,
    string? CustomField,
    string? EventCode,
    string? Status,
    string? TransactionDate,
    decimal? Amount,
    decimal? FeeAmount,
    string? CurrencyCode);

public record PayPalGatewayError(
    int StatusCode,
    string? Name,
    string? Message,
    string? DebugId,
    IReadOnlyList<string> Issues);
