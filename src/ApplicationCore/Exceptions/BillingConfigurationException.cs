using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when subscription billing is not (or is incorrectly) configured, so the capability
/// is unavailable. The rest of the storefront keeps working; only the billing endpoints fail.
/// </summary>
public class BillingConfigurationException : Exception
{
    public BillingConfigurationException(string message, IEnumerable<string>? errors = null)
        : base(message)
    {
        Errors = errors?.ToArray() ?? Array.Empty<string>();
    }

    /// <summary>The individual configuration problems, e.g. which keys are missing.</summary>
    public IReadOnlyCollection<string> Errors { get; }
}
