using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record CardAddress(string CountryCode, string? AddressLine1, string? AddressLine2,
    string? AdminArea2, string? AdminArea1, string? PostalCode);

public sealed record CardInput(string Name, string Number, string Expiry, string SecurityCode,
    CardAddress BillingAddress);

public sealed record PayPalOrderResult(string Id, string Status);
public sealed record PayPalAuthorizationResult(string Id, string Status, decimal Amount,
    string Currency, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
    string? RelatedCaptureId = null);
public sealed record PayPalCaptureResult(string Id, string Status, decimal Amount, string Currency,
    decimal Fee, decimal NetAmount, DateTimeOffset CreatedAt);
public sealed record PayPalRefundResult(string Id, string Status, decimal Amount, string Currency,
    DateTimeOffset CreatedAt);
public sealed record PayPalPaymentTokenResult(string Id, string Brand, string LastDigits, string Expiry);
public sealed record PayPalTransactionRecord(string TransactionId, string? PaypalReferenceId,
    string? PaypalReferenceIdType, string? InvoiceId, string EventCode, string Status,
    decimal Amount, string Currency, decimal? Fee, DateTimeOffset InitiatedAt, DateTimeOffset UpdatedAt);

public sealed class PayPalException : Exception
{
    public PayPalException(HttpStatusCode statusCode, string message, string? debugId,
        IReadOnlyList<string> issues) : base(message)
    {
        StatusCode = statusCode;
        DebugId = debugId;
        Issues = issues;
    }

    public HttpStatusCode StatusCode { get; }
    public string? DebugId { get; }
    public IReadOnlyList<string> Issues { get; }
}

public sealed class PayPalPayerActionRequiredException : Exception
{
    public PayPalPayerActionRequiredException()
        : base("PayPal requires a browser challenge for this card. This API intentionally does not implement a browser approval round-trip.") { }
}

public sealed class PaymentConflictException : Exception
{
    public PaymentConflictException(string message, string? operatorAction = null) : base(message)
    {
        OperatorAction = operatorAction;
    }

    public string? OperatorAction { get; }
}
