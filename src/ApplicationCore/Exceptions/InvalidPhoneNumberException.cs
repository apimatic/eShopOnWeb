using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a number a shopper tries to register is not one the provider considers a usable SMS
/// destination. Surfaced to the caller as a rejection at registration time.
/// </summary>
public class InvalidPhoneNumberException : Exception
{
    public InvalidPhoneNumberException(IReadOnlyList<string> reasons)
        : base(BuildMessage(reasons))
    {
        Reasons = reasons;
    }

    public IReadOnlyList<string> Reasons { get; }

    private static string BuildMessage(IReadOnlyList<string> reasons)
    {
        var detail = reasons is { Count: > 0 } ? string.Join(", ", reasons) : "the provider does not consider it a usable destination";
        return $"The phone number was rejected: {detail}.";
    }
}
