using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public record CardPaymentDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string? Name,
    BillingAddressDetails? BillingAddress);

public record BillingAddressDetails(
    string CountryCode,
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode);

public record AuthorizationResult(
    string PayPalOrderId,
    string OrderStatus,
    string AuthorizationId,
    string AuthorizationStatus,
    DateTimeOffset? ExpirationTime,
    decimal HeldAmount,
    bool RequiresPayerAction);

public record CaptureResult(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal? PaypalFee,
    decimal? NetAmount,
    decimal? GrossAmount,
    bool IsPending);

public record RefundGatewayResult(
    string RefundId,
    string Status,
    decimal Amount);

public record VaultedCardResult(
    string VaultId,
    string? PayPalCustomerId,
    string? MerchantCustomerId,
    string? LastDigits,
    string? Brand,
    string? Expiry,
    string? CardholderName,
    bool RequiresPayerAction);

public record PayPalTransactionRecord(
    string? TransactionId,
    string? PaypalReferenceId,
    string? InvoiceId,
    string? CustomField,
    string? Status,
    string? Amount,
    string? Fee,
    string? InitiationDate);

public record ReconciliationLine(
    string? OrderId,
    string? PayPalTransactionId,
    string Match,
    string? InvoiceId,
    string? Amount,
    string? Status,
    string? Note);
