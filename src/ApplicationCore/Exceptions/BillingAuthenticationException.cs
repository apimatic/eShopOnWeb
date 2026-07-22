using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the provider rejects our credentials (401/403). The offending key is never included.
/// </summary>
public class BillingAuthenticationException : BillingProviderException
{
    public BillingAuthenticationException(string message, int? statusCode = 401, IEnumerable<string>? providerErrors = null)
        : base(message, statusCode, providerErrors)
    {
    }
}
