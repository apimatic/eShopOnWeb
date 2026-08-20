using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public record CardPaymentInput(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? Name,
    BillingAddressInput? BillingAddress);

public record BillingAddressInput(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string? CountryCode);

public record PaypalAuthorizationResult(
    string PaypalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpirationTime,
    decimal AuthorizedAmount,
    string Currency);

public record PaypalAuthorizationDetails(
    string AuthorizationId,
    string Status,
    DateTimeOffset? CreateTime,
    DateTimeOffset? ExpirationTime,
    decimal Amount,
    string Currency);

public record PaypalCaptureResult(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal PaypalFee,
    decimal NetAmount,
    string Currency);

public record PaypalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

public record PaypalVaultedCard(
    string PaymentTokenId,
    string PaypalCustomerId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? Name);

public record PaypalReportedTransaction(
    string TransactionId,
    string? ReferenceId,
    string? ReferenceIdType,
    string? EventCode,
    string? Status,
    DateTimeOffset? InitiationDate,
    decimal? Amount,
    string? Currency,
    decimal? FeeAmount,
    string? InvoiceId,
    string? CustomField);
