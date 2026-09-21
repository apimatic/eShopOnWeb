using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment operation could not be completed. Carries a caller-safe <see cref="Exception.Message"/> and
/// the HTTP <see cref="StatusCode"/> the API should surface, so the mapping from a PayPal/transport
/// failure to a caller-facing status is decided once (in the gateway) and the middleware stays trivial.
///
/// Never carries raw PayPal error bodies or card data — only a sanitized message.
/// </summary>
public class PaymentProcessingException : Exception
{
    public PaymentProcessingException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>The caller-facing HTTP status this failure should map to.</summary>
    public int StatusCode { get; }
}
