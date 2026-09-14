using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// The Maxio API could not be reached at all - DNS, TLS, connection or timeout failure. Distinct
/// from <see cref="MaxioApiException"/>, which means Maxio answered with a failure status.
/// </summary>
public class MaxioTransportException : Exception
{
    public MaxioTransportException(string message, Exception? innerException) : base(message, innerException)
    {
    }
}
