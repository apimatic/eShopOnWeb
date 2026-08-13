using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a shopper tries to register a number the messaging provider does not consider a
/// usable destination. Surfaced at registration time rather than at the moment a message fails.
/// </summary>
public class InvalidContactNumberException : Exception
{
    public InvalidContactNumberException(IReadOnlyList<string> validationErrors)
        : base(BuildMessage(validationErrors))
    {
        ValidationErrors = validationErrors;
    }

    public IReadOnlyList<string> ValidationErrors { get; }

    private static string BuildMessage(IReadOnlyList<string> validationErrors)
    {
        var reasons = validationErrors is { Count: > 0 }
            ? string.Join(", ", validationErrors)
            : "the provider does not consider it a valid destination";
        return $"The phone number cannot be registered: {reasons}.";
    }
}
