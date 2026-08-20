using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payment;

public record BillingAddressInput(
    string Line1,
    string? Line2,
    string City,
    string? State,
    string PostalCode,
    string CountryCode);

public record CardPaymentInput(
    string Number,
    string Expiry,
    string SecurityCode,
    string? Name,
    BillingAddressInput? BillingAddress);

public record AuthorizationResult(
    string PaypalOrderId,
    string AuthorizationId,
    string? Status,
    DateTimeOffset? ExpiresAt);

public record CaptureResult(
    string CaptureId,
    string? Status,
    decimal CapturedAmount,
    decimal? PaypalFee,
    decimal? NetAmount,
    string Currency);

public record RefundResult(
    string RefundId,
    string? Status,
    decimal Amount);

public record VaultResult(
    string VaultId,
    string? PaypalCustomerId,
    string? MerchantCustomerId,
    string? LastDigits,
    string? Brand,
    string? Expiry);

public record GatewayTransaction(
    string TransactionId,
    string? InvoiceId,
    string? CustomField,
    decimal? Amount,
    decimal? Fee,
    string? Status,
    string? ReferenceId);
