using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Sms;

/// <summary>
/// Raised when a call to the messaging provider fails (transport error, or the provider rejects
/// the request). Sending code catches this so that a message that cannot be sent never fails the
/// underlying order operation.
/// </summary>
public class SmsGatewayException : Exception
{
    /// <summary>The provider's error code, when it returned one.</summary>
    public int? ProviderErrorCode { get; }

    public SmsGatewayException(string message, int? providerErrorCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderErrorCode = providerErrorCode;
    }
}
