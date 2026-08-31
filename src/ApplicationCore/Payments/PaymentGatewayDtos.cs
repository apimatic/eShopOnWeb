using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Full card details, held only in memory for the duration of one PayPal call.
/// Never persisted, never logged.
/// </summary>
public record CardDetails(
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

/// <summary>Result of creating a PayPal order with intent=AUTHORIZE.</summary>
public record GatewayAuthorizationResult(
    string PayPalOrderId,
    string OrderStatus,
    string? AuthorizationId,
    string? AuthorizationStatus,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? ExpiresAt);

public record GatewayAuthorization(
    string AuthorizationId,
    string Status,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? ExpiresAt);

public record GatewayCaptureResult(
    string CaptureId,
    string Status,
    decimal Amount,
    string Currency,
    decimal? PayPalFee,
    decimal? NetAmount);

public record GatewayRefundResult(
    string RefundId,
    string Status,
    decimal? Amount,
    string? Currency);

public record GatewayVaultedCard(
    string VaultTokenId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName);

public record GatewayTransaction(
    string TransactionId,
    string? ReferenceId,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? FeeAmount,
    DateTimeOffset? InitiationDate,
    DateTimeOffset? UpdatedDate,
    string? InvoiceId,
    string? CustomField);

public class PayPalApiException : Exception
{
    public PayPalApiException(int statusCode, string? errorName, string message, IReadOnlyList<string>? issues)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorName = errorName;
        Issues = issues ?? Array.Empty<string>();
    }

    public int StatusCode { get; }
    public string? ErrorName { get; }
    public IReadOnlyList<string> Issues { get; }

    public bool HasIssue(string issue) =>
        Issues.Count == 0 ? false : string.Join(';', Issues).Contains(issue, StringComparison.OrdinalIgnoreCase);
}
