using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a call to the messaging provider fails. Its message is deliberately free of any
/// shopper phone number or account secret so it is safe to surface and to log.
/// </summary>
public class SmsProviderException : Exception
{
    /// <summary>The provider's error code, when it returned one.</summary>
    public int? ProviderErrorCode { get; }

    public SmsProviderException(string message, int? providerErrorCode = null, Exception? inner = null)
        : base(message, inner)
    {
        ProviderErrorCode = providerErrorCode;
    }
}
