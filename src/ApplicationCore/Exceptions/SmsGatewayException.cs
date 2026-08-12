using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the SMS provider could not be talked to at all, or answered a request the
/// integration cannot treat as an outcome (e.g. a failed phone-number lookup). Note that a message
/// the provider accepts but a carrier later refuses is <em>not</em> this exception — that is a
/// normal delivery outcome recorded on the notification.
/// </summary>
public class SmsGatewayException : Exception
{
    public int? ProviderErrorCode { get; }

    public SmsGatewayException(string message, int? providerErrorCode = null, Exception? inner = null)
        : base(message, inner)
    {
        ProviderErrorCode = providerErrorCode;
    }
}
