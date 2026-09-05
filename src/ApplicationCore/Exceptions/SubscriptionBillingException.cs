using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing provider (Maxio) rejected the request as invalid, e.g. an unknown plan handle.
/// The messages come directly from the provider and are safe to surface to the caller.
/// </summary>
public class SubscriptionValidationException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public SubscriptionValidationException(IReadOnlyList<string> errors)
        : base(errors.Count > 0 ? string.Join(" ", errors) : "The billing provider rejected the request.")
    {
        Errors = errors;
    }
}

/// <summary>
/// The billing provider (Maxio) could not be reached, or returned an unexpected response.
/// This indicates an upstream/configuration problem, not a bad request from the caller.
/// </summary>
public class SubscriptionProviderException : Exception
{
    public SubscriptionProviderException(string message) : base(message)
    {
    }

    public SubscriptionProviderException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
