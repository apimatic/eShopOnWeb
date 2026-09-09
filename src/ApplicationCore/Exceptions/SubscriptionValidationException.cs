using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a subscription request is rejected for validation reasons — either by
/// eShopOnWeb (e.g. an unknown plan handle) or by Maxio (a 422 response). Surfaced to API
/// callers as a 422 Unprocessable Entity carrying the individual error messages.
/// </summary>
public class SubscriptionValidationException : Exception
{
    public SubscriptionValidationException(IEnumerable<string> errors)
        : base(BuildMessage(errors, out var materialized))
    {
        Errors = materialized;
    }

    public SubscriptionValidationException(string error)
        : this(new[] { error })
    {
    }

    public IReadOnlyList<string> Errors { get; }

    private static string BuildMessage(IEnumerable<string> errors, out IReadOnlyList<string> materialized)
    {
        materialized = (errors ?? Enumerable.Empty<string>())
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .ToList();

        return materialized.Count > 0
            ? string.Join(" ", materialized)
            : "The subscription request was rejected.";
    }
}
