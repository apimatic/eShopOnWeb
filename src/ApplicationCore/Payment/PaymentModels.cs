using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payment;

public record CardPaymentInput(
    string Number,
    string Expiry,
    string SecurityCode,
    string? Name,
    CardBillingAddress? BillingAddress);

public record CardBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string CountryCode);

public record AuthorizationHold(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    string AmountValue,
    string Currency,
    string? ExpirationTime);

public record CaptureProceeds(
    string CaptureId,
    string Status,
    string AmountValue,
    string Currency,
    string? PaypalFeeValue,
    string? NetAmountValue);

public record RefundProceeds(
    string RefundId,
    string Status,
    string AmountValue,
    string Currency);

public record VaultedCard(
    string PayPalPaymentTokenId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName);

public record PayPalReportedTransaction(
    string TransactionId,
    string? InvoiceId,
    string? CustomField,
    string? Status,
    string? AmountValue,
    string? FeeAmountValue,
    string? Currency,
    string? InitiationDate,
    string? PaypalReferenceId);

public record TransactionSearchPage(
    IReadOnlyList<PayPalReportedTransaction> Transactions,
    int? TotalPages,
    string? LastRefreshedDatetime);
