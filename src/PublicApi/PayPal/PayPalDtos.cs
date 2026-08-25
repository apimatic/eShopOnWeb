using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.PayPal;

public record CardPaymentDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string CardholderName,
    string AddressLine1,
    string City,
    string State,
    string CountryCode,
    string PostalCode);

public record AuthorizeResult(string PayPalOrderId, string AuthorizationId);

public record CaptureResult(
    string CaptureId,
    decimal CapturedAmount,
    decimal? Fee,
    decimal? Net,
    string? NewAuthorizationId = null);

public record RefundResult(string RefundId, decimal Amount);

public record VaultResult(
    string TokenId,
    string? CustomerId,
    string? LastFour,
    string? Brand,
    string? Expiry);

public record VaultedCardInfo(
    string TokenId,
    string? LastFour,
    string? Brand,
    string? Expiry);

public record TransactionRecord(
    string? TransactionId,
    string? Amount,
    string? Currency,
    string? Status,
    string? PayPalReferenceId,
    string? ReferenceType,
    DateTimeOffset? Timestamp);
