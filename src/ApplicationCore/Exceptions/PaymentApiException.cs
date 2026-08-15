using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A business/flow error raised by the payment feature that maps directly onto an HTTP status
/// (404 not found, 403 forbidden, 409 conflict, 400/422 invalid). Carries a caller-safe message
/// only — never provider internals.
/// </summary>
public class PaymentApiException : Exception
{
    public PaymentApiException(int statusCode, string message, string? errorCode = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public PaymentApiException(int statusCode, string message, string? errorCode, Exception inner)
        : base(message, inner)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    /// <summary>HTTP status code this error should surface as.</summary>
    public int StatusCode { get; }

    /// <summary>Optional machine-readable code (e.g. a PayPal issue token) for operators.</summary>
    public string? ErrorCode { get; }

    public static PaymentApiException NotFound(string message) => new(404, message);
    public static PaymentApiException Conflict(string message) => new(409, message);
    public static PaymentApiException BadRequest(string message) => new(400, message);
}
