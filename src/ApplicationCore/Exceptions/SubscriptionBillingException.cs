using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the billing system rejects a request or is unreachable. Carries the underlying
/// provider error messages (if any) so they can be surfaced to callers.
/// </summary>
public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(string message, IReadOnlyCollection<string>? errors = null, Exception? innerException = null)
        : base(BuildMessage(message, errors), innerException)
    {
        Errors = errors ?? Array.Empty<string>();
    }

    public IReadOnlyCollection<string> Errors { get; }

    private static string BuildMessage(string message, IReadOnlyCollection<string>? errors)
    {
        if (errors is null || errors.Count == 0)
        {
            return message;
        }

        return $"{message}: {string.Join("; ", errors.Where(e => !string.IsNullOrWhiteSpace(e)))}";
    }
}
