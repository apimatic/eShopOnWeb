using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing provider deterministically rejected the request. Retrying the identical request
/// cannot succeed, so this must not be reported to callers as a transient outage.
/// </summary>
public class BillingValidationException : BillingException
{
    public BillingValidationException(string message, IEnumerable<string>? errors = null, Exception? innerException = null, int? providerStatusCode = null)
        : base(message, innerException, providerStatusCode)
    {
        Errors = errors?.Where(e => !string.IsNullOrWhiteSpace(e)).ToArray() ?? Array.Empty<string>();
    }

    /// <summary>Provider-supplied validation messages, when the provider sent any.</summary>
    public IReadOnlyCollection<string> Errors { get; }
}
