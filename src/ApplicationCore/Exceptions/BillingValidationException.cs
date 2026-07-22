using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the provider refuses a call because the request itself was invalid - a bad quantity,
/// an illegal state transition, an unknown handle in the payload.
/// </summary>
public class BillingValidationException : BillingProviderException
{
    public BillingValidationException(string message, int? statusCode = 422, IEnumerable<string>? providerErrors = null)
        : base(message, statusCode, providerErrors)
    {
    }
}
