using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the SMS provider returns an error for a messaging call. Carries the provider's
/// error code when one was supplied so callers can branch on it. The message never contains a
/// destination number.
/// </summary>
public class SmsGatewayException : Exception
{
    public SmsGatewayException(string message, int? providerErrorCode = null) : base(message)
    {
        ProviderErrorCode = providerErrorCode;
    }

    public SmsGatewayException(string message, Exception innerException, int? providerErrorCode = null)
        : base(message, innerException)
    {
        ProviderErrorCode = providerErrorCode;
    }

    public int? ProviderErrorCode { get; }
}
