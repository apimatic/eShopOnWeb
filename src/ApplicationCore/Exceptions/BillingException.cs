using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a subscription-billing operation cannot be completed. <see cref="StatusCode"/>
/// carries the HTTP status the API surface should return: 400 for caller/validation problems
/// (e.g. an unknown plan handle), 502 when the upstream billing system fails or misbehaves.
/// </summary>
public class BillingException : Exception
{
    public int StatusCode { get; }

    public BillingException(string message, int statusCode = 502, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>A caller/validation error (HTTP 400).</summary>
    public static BillingException Validation(string message) => new(message, statusCode: 400);

    /// <summary>An upstream billing-system failure (HTTP 502).</summary>
    public static BillingException Upstream(string message, Exception? innerException = null) =>
        new(message, statusCode: 502, innerException);
}
