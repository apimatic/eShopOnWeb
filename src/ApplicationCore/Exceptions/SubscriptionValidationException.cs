using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing provider rejected a subscription request as invalid (e.g. unknown plan handle,
/// a plan that requires a payment method, or a provider-side validation error).
/// </summary>
public class SubscriptionValidationException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public SubscriptionValidationException(IReadOnlyList<string> errors)
        : base(errors.Count > 0 ? string.Join(" ", errors) : "The subscription request was rejected by the billing provider.")
    {
        Errors = errors;
    }

    public SubscriptionValidationException(string error) : this(new[] { error })
    {
    }
}
