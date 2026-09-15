using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A real failure interacting with the messaging provider — it was unreachable, timed out, or
/// returned an error status. This is distinct from an <em>accepted</em> message whose delivery
/// outcome is merely undeliverable; that is reported through the result, not thrown.
///
/// <see cref="StatusCode"/> carries the provider's HTTP status when one was returned (null for a
/// transport failure), so a caller-facing boundary can map it deliberately.
/// </summary>
public class SmsGatewayException : Exception
{
    public int? StatusCode { get; }

    public SmsGatewayException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
