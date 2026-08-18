using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the SMS provider gateway raises. It carries the provider's HTTP status (when the
/// provider actually answered) so the API boundary can map a caller-caused rejection (4xx) back to the caller
/// while treating our-credentials/quota and transport failures as provider unavailability. The message is
/// always caller-safe and never contains a phone number or a secret.
/// </summary>
public class SmsProviderException : Exception
{
    public SmsProviderException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>The provider's HTTP status, or null when nothing answered (transport failure / timeout).</summary>
    public HttpStatusCode? StatusCode { get; }
}
