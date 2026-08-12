using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a submitted phone number is rejected by the messaging provider as not a usable
/// destination. The message is caller-safe and never contains the submitted number. Maps to HTTP 400.
/// </summary>
public class InvalidPhoneNumberException : Exception
{
    public IReadOnlyList<string> Reasons { get; }

    public InvalidPhoneNumberException(IReadOnlyList<string>? reasons = null)
        : base("The supplied phone number is not a valid, reachable destination.")
    {
        Reasons = reasons ?? Array.Empty<string>();
    }
}
