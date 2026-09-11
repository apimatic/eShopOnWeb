using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A business-level failure in a payment operation, carrying the HTTP status the API should
/// return and a message safe (and useful) to show the caller/operator.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(int statusCode, string message, IReadOnlyList<string>? details = null)
        : base(message)
    {
        StatusCode = statusCode;
        Details = details ?? Array.Empty<string>();
    }

    public int StatusCode { get; }
    public IReadOnlyList<string> Details { get; }

    public static PaymentException NotFound(string message) => new(404, message);
    public static PaymentException Forbidden(string message) => new(403, message);
    public static PaymentException Conflict(string message, IReadOnlyList<string>? details = null) => new(409, message, details);
    public static PaymentException Declined(string message, IReadOnlyList<string>? details = null) => new(402, message, details);
    public static PaymentException Invalid(string message) => new(400, message);
}
