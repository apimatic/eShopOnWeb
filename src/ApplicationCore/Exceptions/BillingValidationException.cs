using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing provider rejected the request as invalid (e.g. a missing payment method, or a
/// customer attribute that failed validation). Retrying the identical request will not help.
/// </summary>
public class BillingValidationException : BillingException
{
    public BillingValidationException(string message, IEnumerable<string>? errors = null)
        : base(message)
    {
        Errors = errors?.Where(e => !string.IsNullOrWhiteSpace(e)).ToArray() ?? Array.Empty<string>();
    }

    /// <summary>The individual validation messages reported by the provider.</summary>
    public IReadOnlyList<string> Errors { get; }
}
