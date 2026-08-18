using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the SMS provider could not be reached or its response could not be processed
/// (transport failure, authentication/configuration failure, or an unreadable body) — i.e. cases
/// where we obtained no provider state at all. A message the provider accepted but the carrier then
/// refused is NOT this exception: that comes back as a normal result carrying an "undelivered"/"failed"
/// status.
///
/// Send paths deliberately catch this so that a messaging failure never fails the underlying order
/// operation; report/validation paths let it surface as an upstream error.
/// </summary>
public class SmsProviderException : Exception
{
    public SmsProviderException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>The provider's HTTP status, when the provider answered with an error status.</summary>
    public int? StatusCode { get; }
}
