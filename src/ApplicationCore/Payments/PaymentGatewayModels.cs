using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed record PaymentCard(
    string Number,
    string Expiry,
    string SecurityCode,
    string Name,
    PaymentBillingAddress BillingAddress);

public sealed record PaymentBillingAddress(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string State,
    string PostalCode,
    string CountryCode);

public sealed record PaymentSource(PaymentCard? Card, string? VaultId)
{
    public static PaymentSource FromCard(PaymentCard card) => new(card, null);
    public static PaymentSource FromVault(string vaultId) => new(null, vaultId);
}

public sealed record GatewayAuthorization(
    string PayPalOrderId,
    string PayPalOrderStatus,
    string AuthorizationId,
    string AuthorizationStatus,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt);

public sealed record GatewayCapture(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    decimal? PayPalFee,
    decimal? NetAmount,
    DateTimeOffset CreatedAt);

public sealed record GatewayRefund(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt);

public sealed record GatewaySavedCard(
    string PaymentTokenId,
    string? CustomerId,
    string Brand,
    string Last4,
    string Expiry);

public sealed record GatewayTransaction(
    string TransactionId,
    string? PayPalReferenceId,
    string? PayPalReferenceIdType,
    string EventCode,
    string Status,
    DateTimeOffset? InitiatedAt,
    DateTimeOffset? UpdatedAt,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    string? InvoiceId);

public sealed class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string operation, int statusCode, string code,
        string message, IReadOnlyCollection<string> issues, string? debugId)
        : base(message)
    {
        Operation = operation;
        StatusCode = statusCode;
        Code = code;
        Issues = issues;
        DebugId = debugId;
    }

    public string Operation { get; }
    public int StatusCode { get; }
    public string Code { get; }
    public IReadOnlyCollection<string> Issues { get; }
    public string? DebugId { get; }

    public bool HasIssue(string issue)
    {
        foreach (var current in Issues)
        {
            if (string.Equals(current, issue, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }
}
