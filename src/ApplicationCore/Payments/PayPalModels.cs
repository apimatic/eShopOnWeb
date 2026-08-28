using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed record PayPalCard(
    string Name,
    string Number,
    string Expiry,
    string SecurityCode,
    PayPalBillingAddress BillingAddress);

public sealed record PayPalBillingAddress(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string State,
    string PostalCode,
    string CountryCode);

public sealed record PayPalOrderResult(string Id, string Status);

public sealed record PayPalAuthorizationResult(
    string OrderId,
    string OrderStatus,
    string AuthorizationId,
    string AuthorizationStatus,
    decimal Amount,
    string Currency,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ExpiresAt);

public sealed record PayPalCaptureResult(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    decimal? Fee,
    decimal? NetAmount,
    DateTimeOffset? CreatedAt);

public sealed record PayPalRefundResult(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? CreatedAt);

public sealed record PayPalSavedCardResult(
    string TokenId,
    string? CustomerId,
    string Brand,
    string LastDigits,
    string Expiry);

public sealed record PayPalTransaction(
    string TransactionId,
    string? PayPalReferenceId,
    string? EventCode,
    string? Status,
    DateTimeOffset? InitiatedAt,
    decimal Amount,
    string Currency,
    decimal? Fee,
    string? InvoiceId,
    string? CustomField);

public sealed class PayPalApiException : Exception
{
    public PayPalApiException(HttpStatusCode statusCode, string name, string message,
        string? debugId, IReadOnlyCollection<string> issues, bool requiresPayerAction = false)
        : base(message)
    {
        StatusCode = statusCode;
        Name = name;
        DebugId = debugId;
        Issues = issues;
        RequiresPayerAction = requiresPayerAction;
    }

    public HttpStatusCode StatusCode { get; }
    public string Name { get; }
    public string? DebugId { get; }
    public IReadOnlyCollection<string> Issues { get; }
    public bool RequiresPayerAction { get; }
}
