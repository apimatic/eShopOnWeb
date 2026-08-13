using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Raised by the messaging gateway when the provider refuses a request. Its <see cref="Message"/> is
/// already scrubbed of anything phone-number-like so it is safe to log or store as an outcome.
/// </summary>
public class SmsGatewayException : Exception
{
    public SmsGatewayException(string scrubbedMessage, int? providerErrorCode = null)
        : base(scrubbedMessage)
    {
        ProviderErrorCode = providerErrorCode;
    }

    /// <summary>The provider's own error code, when it supplied one.</summary>
    public int? ProviderErrorCode { get; }
}
