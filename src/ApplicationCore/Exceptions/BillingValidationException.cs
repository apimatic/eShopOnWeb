using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing provider rejected the request as unprocessable. This is a problem with what was
/// asked for (bad plan, invalid customer data) rather than with the provider, so it surfaces to
/// the caller as a client error rather than a gateway failure.
/// </summary>
public class BillingValidationException : BillingProviderException
{
    public BillingValidationException(string message, int? providerStatusCode = null, IReadOnlyList<string>? providerErrors = null)
        : base(message, providerStatusCode, providerErrors)
    {
    }
}
