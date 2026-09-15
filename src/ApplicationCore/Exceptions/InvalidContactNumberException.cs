using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a shopper tries to register a number the messaging provider does not consider a
/// usable destination. The offending number is deliberately not included in the message, so it
/// never reaches a log.
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
            : "the provider rejected it as not reachable";
        return $"The phone number is not a usable destination ({reasons}).";
    }
}
