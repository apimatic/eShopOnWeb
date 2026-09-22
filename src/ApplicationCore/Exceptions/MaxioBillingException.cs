using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the subscription-billing abstraction raises. Callers (e.g. the PublicApi
/// endpoints) map this to a caller-facing status without ever seeing a Maxio SDK or transport exception.
/// </summary>
public class MaxioBillingException : Exception
{
    /// <summary>
    /// The provider HTTP status when one is known (Maxio "Case B" raw errors carry it). Null when the
    /// failure was a transport error or a typed error body that carried no status — i.e. an unknown, not
    /// a caller error.
    /// </summary>
    public int? StatusCode { get; }

    public MaxioBillingException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
