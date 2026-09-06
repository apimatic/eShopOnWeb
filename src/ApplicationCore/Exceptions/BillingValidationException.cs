using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing system rejected the request as invalid, e.g. a plan that requires a payment method
/// when none was supplied. Retrying the same request will fail the same way.
/// </summary>
public class BillingValidationException : BillingException
{
    public BillingValidationException(string message, IEnumerable<string>? errors = null)
        : base(message)
    {
        Errors = errors?.ToList() ?? new List<string>();
    }

    public IReadOnlyCollection<string> Errors { get; }
}
