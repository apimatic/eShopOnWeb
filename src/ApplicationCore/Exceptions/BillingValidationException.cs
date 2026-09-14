using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing system rejected the request as invalid (for example, a plan that requires a payment
/// method the shopper has not supplied). The messages come from the billing system verbatim.
/// </summary>
public class BillingValidationException : Exception
{
    public BillingValidationException(IReadOnlyList<string> errors)
        : base(errors.Count > 0
            ? string.Join(" ", errors)
            : "The billing system rejected the request.")
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }

    public override string ToString() => $"{base.ToString()} Errors: [{string.Join("; ", Errors.Select(e => e))}]";
}
