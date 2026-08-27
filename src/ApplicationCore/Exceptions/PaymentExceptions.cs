using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The requested resource does not exist, or does not belong to the caller. Maps to HTTP 404.</summary>
public class ResourceNotFoundException : Exception
{
    public ResourceNotFoundException(string message) : base(message) { }
}

/// <summary>
/// The requested payment action conflicts with the current state of the order/payment
/// (e.g. capturing a cancelled order, refunding more than was captured, an authorization
/// that can no longer be renewed). Maps to HTTP 409.
/// </summary>
public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message) : base(message) { }
}

/// <summary>The payment provider declined the authorization or capture. Maps to HTTP 402.</summary>
public class PaymentDeclinedException : Exception
{
    public PaymentDeclinedException(string message) : base(message) { }
}

/// <summary>
/// The payment provider requires the shopper to approve the payment in a browser
/// (e.g. a 3-D Secure challenge). This integration is server-to-server only and does
/// not build an approval round-trip. Maps to HTTP 409.
/// </summary>
public class PaymentRequiresBuyerActionException : Exception
{
    public PaymentRequiresBuyerActionException(string message) : base(message) { }
}

/// <summary>
/// A call to the PayPal API failed. Carries PayPal's error name/issue/debug id for
/// correlation. Never carries card details. Maps to HTTP 502.
/// </summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(HttpStatusCode statusCode, string? errorName, string? issue, string? debugId, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorName = errorName;
        Issue = issue;
        DebugId = debugId;
    }

    public HttpStatusCode StatusCode { get; }
    public string? ErrorName { get; }
    public string? Issue { get; }
    public string? DebugId { get; }

    public bool IsClientError => (int)StatusCode >= 400 && (int)StatusCode < 500;
}
