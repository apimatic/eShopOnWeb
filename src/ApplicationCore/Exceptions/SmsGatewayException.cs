using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure talking to the SMS provider that is not a normal message outcome — an outage, an
/// authentication problem, a transport failure, or an unreadable response. Notification flows catch
/// this and record the failure without ever failing the underlying order operation.
/// </summary>
public class SmsGatewayException : Exception
{
    /// <summary>HTTP status the provider returned, when there was one; null for transport failures.</summary>
    public int? StatusCode { get; }

    /// <summary>The provider's own error code, when the error body carried one.</summary>
    public int? ProviderErrorCode { get; }

    public SmsGatewayException(string message, int? statusCode = null, int? providerErrorCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ProviderErrorCode = providerErrorCode;
    }
}
