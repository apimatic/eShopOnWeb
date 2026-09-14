using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing system rejected the request on business rules, for example because the plan requires a
/// payment method that has not been captured. Retrying without changing the request will not help.
/// </summary>
public class SubscriptionBillingValidationException : SubscriptionBillingException
{
    public SubscriptionBillingValidationException(IReadOnlyList<string> errors)
        : base(errors.Count > 0 ? string.Join(" ", errors) : "The billing system rejected the request.")
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}
