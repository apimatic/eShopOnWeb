using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment could not be carried out. The <see cref="Message"/> is caller-safe
/// (it never contains card data or raw provider payloads) and <see cref="StatusCode"/>
/// carries the HTTP status the API should answer with:
///   * 4xx  — the request itself was rejected (bad state, validation, conflict);
///   * 502  — the payment provider was unreachable or returned something unusable.
/// </summary>
public class PaymentException : Exception
{
    public int StatusCode { get; }

    public PaymentException(string message, int statusCode = 400)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public PaymentException(string message, int statusCode, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
