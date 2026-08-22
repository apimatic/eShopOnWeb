using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public record MoneyAmount(decimal Value, string Currency);

public record BillingAddress(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string State,
    string PostalCode,
    string CountryCode);

public record CardPaymentSource(
    string Number,
    string Expiry,
    string SecurityCode,
    string Name,
    BillingAddress? Address);

public record AuthorizationResult(
    string PayPalOrderId,
    string PayPalOrderStatus,
    string AuthorizationId,
    string AuthorizationStatus,
    DateTimeOffset? ExpirationTime,
    MoneyAmount Amount);

public record AuthorizationDetails(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpirationTime,
    MoneyAmount? Amount);

public record CaptureResult(
    string CaptureId,
    string Status,
    MoneyAmount CapturedAmount,
    MoneyAmount? PaypalFee,
    MoneyAmount? NetAmount);

public record RefundGatewayResult(
    string PayPalRefundId,
    string Status,
    MoneyAmount Amount);

public record VaultedCardResult(
    string VaultId,
    string? LastDigits,
    string? Brand,
    string? Expiry,
    string? Name);

public record GatewayTransaction(
    string TransactionId,
    string? ReferenceId,
    string? CustomField,
    string? InvoiceId,
    string? EventCode,
    string? Status,
    DateTimeOffset? InitiationDate,
    MoneyAmount? Amount,
    MoneyAmount? Fee);

public record TransactionSearchResult(
    IReadOnlyList<GatewayTransaction> Transactions,
    DateTimeOffset? LastRefreshed);
