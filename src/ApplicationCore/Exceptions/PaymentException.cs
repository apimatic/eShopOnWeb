using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment/domain rule was violated (e.g. refunding more than was captured, paying an order that
/// is already paid). Carries the HTTP status the API boundary should surface.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message, int statusCode = 400) : base(message)
    {
        StatusCode = statusCode;
    }

    /// <summary>The HTTP status code this failure maps to at the API boundary.</summary>
    public int StatusCode { get; }
}
